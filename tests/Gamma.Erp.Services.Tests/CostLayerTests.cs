using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>
/// Cost layers, first in, first out, per place (H12, the founder's rule): the oldest purchase goes first, a transfer carries its layers, a
/// place may give more than it holds only when the company holds it elsewhere, what comes in next settles it, and reversals put back or
/// take off exactly what the reversed line moved.
/// </summary>
public sealed class CostLayerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        await using var context = Context(new TenantContext());
        await context.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private ErpDbContext Context(TenantContext tenant) => new((DbContextOptions<ErpDbContext>)PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(),
        _db.GetConnectionString()).AddInterceptors(new TenantConnectionInterceptor(tenant), new HistoryInterceptor(tenant, TimeProvider.System)).Options);

    [Fact]
    public async Task Cola_bought_at_1_then_at_2_is_sold_at_1_first_carried_with_its_cost_owed_when_short_and_settled_by_the_next_purchase()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", [("cost_center_type:WAREHOUSE", "WH1", "Magazina 1"), ("cost_center_type:WAREHOUSE", "WH2", "Magazina 2")]);
        OrganisationIds company;
        Guid user;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            db.Add(staff);
            await db.SaveChangesAsync(ct);
            var owner = new User { TypeId = staff.Id, Code = "OWNER", Name = TranslatedText.English("Owner"), TenantId = tenantId };
            db.Add(owner);
            await db.SaveChangesAsync(ct);
            user = owner.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var ltd = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id;
        var client = new Partner { TypeId = ltd, Code = "C1", Name = TranslatedText.English("Bar Lura"), TenantId = tenantId, OwnerId = company.Corporate };
        var supplier = new Partner { TypeId = ltd, Code = "S1", Name = TranslatedText.English("Coca-Cola Albania"), TenantId = tenantId, OwnerId = company.Corporate };
        var goods = await context.Set<ProductCategory>().SingleAsync(c => c.Code == "PRODUCT", ct);
        var goodsType = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == goods.Id).Select(l => l.TypeId).FirstAsync(ct);
        var cola = new Product { TypeId = goodsType, Code = "COLA", Name = TranslatedText.English("Cola"), TenantId = tenantId, OwnerId = company.Corporate };
        context.AddRange(client, supplier, cola, new InterProductCategory { ElementId = cola.Id, CategoryId = goods.Id, TenantId = tenantId, OwnerId = company.Corporate });
        await context.SaveChangesAsync(ct);
        var (wh1, wh2) = (await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH1", ct), await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH2", ct));
        var unit = (await context.Set<Unit>().FirstAsync(ct)).Id;
        var item = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ITEM", ct)).Id;
        var vat0 = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 0, ct)).Id;
        var vat20 = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id;
        var types = await context.Set<DocumentType>().ToListAsync(ct);
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);

        async Task<Guid> PostAsync(string code, (Guid Id, string Name) from, (Guid Id, string Name) to, decimal quantity, decimal price, int day)
        {
            var type = types.Single(t => t.Code == code && t.IdSup == null);
            var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == type.Id).Select(l => l.CategoryId).FirstAsync(ct);
            var date = new DateOnly(2026, 10, day);
            var saved = await documents.SaveAsync(new DocumentDto
            {
                TypeId = type.Id, CategoryIds = [category], DocumentDate = date, PostingDate = date, FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name,
                Transactions = [new TransactionDto { TypeId = item, ElementId = cola.Id, ElementTypeId = cola.TypeId, ElementName = "Cola", UnitId = unit, Quantity = quantity,
                    Price = price, VatGroupId = code == "FSHM" ? vat20 : vat0 }],
            }, ct);
            await documents.PostAsync(saved.Id!.Value, ct);
            context.ChangeTracker.Clear();
            return saved.Id!.Value;
        }

        async Task<Guid> ReverseAsync(Guid document, decimal quantity, int day)
        {
            var line = await context.Set<Transaction>().Where(t => t.DocumentId == document).Select(t => t.Id).SingleAsync(ct);
            var reversal = await documents.ReverseAsync(document, new Dictionary<Guid, decimal> { [line] = quantity }, new DateOnly(2026, 10, day), ct);
            context.ChangeTracker.Clear();
            return reversal.Id!.Value;
        }

        async Task<decimal> CostAsync(Guid document) => await context.Set<Transaction>().Where(t => t.DocumentId == document).SumAsync(t => t.CostLcu ?? 0, ct);

        async Task<List<(Guid Holder, decimal Left, decimal Value)>> LayersAsync() =>
            [.. (await context.Set<ProductCostLayer>().Where(l => l.QuantityLeft != 0).OrderBy(l => l.LayerDate).ThenBy(l => l.CreatedOn).ToListAsync(ct))
                .Select(l => (l.HolderId, l.QuantityLeft, l.ValueLeftLcu))];

        var (store1, store2, bar, coke) = ((wh1.Id, "Magazina 1"), (wh2.Id, "Magazina 2"), (client.Id, "Bar Lura"), (supplier.Id, "Coca-Cola Albania"));
        var cheap = await PostAsync("FBM", coke, store1, 10, 1, 1);
        var dear = await PostAsync("FBM", coke, store1, 5, 2, 2);
        (await LayersAsync()).Should().Equal([(wh1.Id, 10m, 10m), (wh1.Id, 5m, 10m)], "x colas at 1 € and y at 2 €");

        // 12 sold: the 10 at 1 € first, then 2 at 2 €.
        var sale = await PostAsync("FSHM", store1, bar, 12, 3, 3);
        (await CostAsync(sale)).Should().Be(14m);
        (await LayersAsync()).Should().Equal([(wh1.Id, 3m, 6m)]);

        // 2 of them come back: the last ones taken (at 2 €) go back into their layer.
        var back = await ReverseAsync(sale, 2, 3);
        (await CostAsync(back)).Should().Be(4m);
        (await LayersAsync()).Should().Equal([(wh1.Id, 5m, 10m)]);

        // 3 carried to Magazina 2 keep their cost and their purchase date.
        await PostAsync("FTM", store1, store2, 3, 2, 4);
        (await LayersAsync()).Should().BeEquivalentTo([(wh1.Id, 2m, 4m), (wh2.Id, 3m, 6m)]);
        (await context.Set<ProductCostLayer>().SingleAsync(l => l.HolderId == wh2.Id, ct)).LayerDate.Should().Be(new DateOnly(2026, 10, 2));

        // Magazina 2 sells 5 with 3: the company holds the 2 more in Magazina 1, so the place owes 2, valued at 2 € (the oldest held elsewhere).
        var short5 = await PostAsync("FSHM", store2, bar, 5, 3, 5);
        (await CostAsync(short5)).Should().Be(10m);
        (await LayersAsync()).Should().BeEquivalentTo([(wh1.Id, 2m, 4m), (wh2.Id, -2m, -4m)]);

        // Nothing more can go out than the company holds (2 in Magazina 1, −2 owed in Magazina 2).
        var none = async () => await PostAsync("FSHM", store1, bar, 3, 3, 5);
        (await none.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.StockError);
        context.ChangeTracker.Clear();

        // 4 bought at 3 € into Magazina 2: 2 settle what it owes (2 € more than valued go into the product's value), 2 stay at 3 €.
        var settling = await PostAsync("FBM", coke, store2, 4, 3, 6);
        (await LayersAsync()).Should().BeEquivalentTo([(wh1.Id, 2m, 4m), (wh2.Id, 2m, 6m)]);
        (await context.Set<ProductCostLayerUse>().SingleAsync(u => u.DocumentId == settling && u.Kind == 4, ct)).ValueLcu.Should().Be(2m);

        // The purchase at 1 € is sold out: it can't be reversed; the one at 2 € still has 2 in Magazina 1, so 2 can, not 3.
        var sold = async () => await ReverseAsync(cheap, 1, 7);
        (await sold.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.StockError);
        context.ChangeTracker.Clear();
        var three = async () => await ReverseAsync(dear, 3, 7);
        (await three.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.StockError);
        context.ChangeTracker.Clear();
        await ReverseAsync(dear, 2, 7);
        (await LayersAsync()).Should().Equal([(wh2.Id, 2m, 6m)]);

        // The product's value is what the layers hold: 2 colas at 3 €.
        var value = await (from b in context.Set<ProductBalance>() join s in context.Set<InterProductSpecificUnit>() on b.ProductSpecificUnitId equals s.Id
                           where s.ProductId == cola.Id select (b.DebitLcu ?? 0) - (b.CreditLcu ?? 0)).SumAsync(ct);
        value.Should().Be(6m);
    }
}

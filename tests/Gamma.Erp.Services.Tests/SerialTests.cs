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
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>Serial numbers (the founder, 2026-11-01): a register under the product, one number per unit on a line, one unit in one place, a reversal puts it back.</summary>
public sealed class SerialTests : IAsyncLifetime
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
    public async Task A_tracked_product_moves_unit_by_unit_each_number_in_one_place_and_a_reversal_puts_it_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", [("cost_center_type:WAREHOUSE", "WH1", "Magazina 1"), ("cost_center_type:WAREHOUSE", "WH2", "Magazina 2")]);
        OrganisationIds company;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            await db.SaveChangesAsync(ct);
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, null);
        await using var context = Context(tenant);
        var owner = company.Corporate;
        var request = new RequestContext(tenantId, null, owner, new HashSet<Guid>(), IsAdministrator: true);
        var visibility = new Visibility(context, request);
        var documents = new DocumentService(context, request, visibility, TimeProvider.System);
        var serials = new SerialService(context, request, visibility);
        var ltd = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id;
        var client = new Partner { TypeId = ltd, Code = "C1", Name = TranslatedText.English("Bar Lura"), TenantId = tenantId, OwnerId = owner };
        var supplier = new Partner { TypeId = ltd, Code = "S1", Name = TranslatedText.English("Furnitori"), TenantId = tenantId, OwnerId = owner };
        var goods = await context.Set<ProductCategory>().SingleAsync(c => c.Code == "PRODUCT", ct);
        var goodsType = await context.Set<ProductType>().FirstAsync(t => context.Set<InterProductTypeCategory>().Any(l => l.CategoryId == goods.Id && l.TypeId == t.Id), ct);
        var router = new Product { TypeId = goodsType.Id, Code = "ROUTER", Name = TranslatedText.English("Router"), TenantId = tenantId, OwnerId = owner };
        context.AddRange(client, supplier, router, new InterProductCategory { ElementId = router.Id, CategoryId = goods.Id, TenantId = tenantId, OwnerId = owner });
        await context.SaveChangesAsync(ct);
        var (first, second) = (await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH1", ct), await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH2", ct));
        var unit = (await context.Set<Unit>().SingleAsync(u => u.Code == "PIECE", ct)).Id;
        var item = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ITEM", ct)).Id;
        var (vat0, vat20) = ((await context.Set<VatGroup>().SingleAsync(v => v.Rate == 0, ct)).Id, (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id);
        var types = await context.Set<DocumentType>().ToListAsync(ct);

        async Task<DocumentDto> MakeAsync(string code, (Guid Id, string Name) from, (Guid Id, string Name) to, decimal quantity, params string[] numbers)
        {
            var type = types.Single(t => t.Code == code && t.IdSup == null);
            var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == type.Id).Select(l => l.CategoryId).FirstAsync(ct);
            var date = new DateOnly(2026, 10, 1);
            return await documents.SaveAsync(new DocumentDto
            {
                TypeId = type.Id, CategoryIds = [category], DocumentDate = date, PostingDate = date, FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name,
                Transactions = [new TransactionDto { TypeId = item, ElementId = router.Id, ElementTypeId = router.TypeId, ElementName = "Router", UnitId = unit, Quantity = quantity, Price = 100, VatGroupId = code == "FTM" ? vat0 : vat20,
                    Serials = [.. numbers.Select(n => new TransactionSerialDto(n, "P-" + n))] }],
            }, ct);
        }

        async Task<DocumentDto> PostAsync(string code, (Guid Id, string Name) from, (Guid Id, string Name) to, decimal quantity, params string[] numbers) =>
            await documents.PostAsync((await MakeAsync(code, from, to, quantity, numbers)).Id!.Value, ct);

        async Task<string?> HolderAsync(string serial) => (await serials.SerialsAsync(router.Id, null, serial, "en", ct)).Single().Holder;

        // Before the type tracks them, stock comes in without numbers: 5 held, none numbered.
        (Guid, string) warehouse1 = (first.Id, "Magazina 1"), warehouse2 = (second.Id, "Magazina 2"), buyer = (client.Id, "Bar Lura"), maker = (supplier.Id, "Furnitori");
        await PostAsync("FBM", maker, warehouse1, 5);
        goodsType.TracksSerial = true;
        await context.SaveChangesAsync(ct);

        // Stock already held gets its numbers: never more than it holds, each once.
        var over = async () => await serials.AssignAsync(new(first.Id, router.Id, unit, [.. Enumerable.Range(1, 6).Select(i => new TransactionSerialDto($"O{i}"))]), "en", ct);
        await over.Should().ThrowAsync<ValidationException>();
        var twice = async () => await serials.AssignAsync(new(first.Id, router.Id, unit, [new("O1"), new("o1")]), "en", ct);
        await twice.Should().ThrowAsync<ValidationException>();
        (await serials.AssignAsync(new(first.Id, router.Id, unit, [new("O1", "PN"), new(" O2 ")]), "en", ct)).Select(s => (s.Serial, s.Holder)).Should().Equal(("O1", "Magazina 1"), ("O2", "Magazina 1"));
        var stock = (await serials.StockAsync("en", ct)).Should().ContainSingle().Which;
        (stock.Quantity, stock.Serials).Should().Be((5m, 2));
        (await serials.AssignAsync(new(first.Id, router.Id, unit, [new("O3"), new("O4"), new("O5")]), "en", ct)).Should().HaveCount(5);
        var again = async () => await serials.AssignAsync(new(first.Id, router.Id, unit, [new("O1")]), "en", ct);
        await again.Should().ThrowAsync<ValidationException>();

        // A purchase names one number per unit, new ones or it is refused.
        foreach (var (quantity, numbers) in new (decimal, string[])[] { (2, []), (2, ["A1"]), (2, ["A1", "A1"]), (1, ["O1"]) })
        {
            var refused = async () => await PostAsync("FBM", maker, warehouse1, quantity, numbers);
            await refused.Should().ThrowAsync<ValidationException>();
            context.ChangeTracker.Clear();
        }

        await PostAsync("FBM", maker, warehouse1, 3, "A1", "A2", "A3");
        await HolderAsync("A2").ContinueWith(h => h.Result.Should().Be("Magazina 1"), ct);
        (await serials.SerialsAsync(null, first.Id, "P-A", "en", ct)).Should().HaveCount(3, "found by part number too");

        // A draft keeps its numbers; a sale gives up only what the place holds.
        var draft = await MakeAsync("FSHM", warehouse1, buyer, 2, "A1", "A2");
        draft.Transactions[0].TracksSerial.Should().BeTrue();
        draft.Transactions[0].Serials.Should().BeEquivalentTo([new TransactionSerialDto("A1", "P-A1"), new TransactionSerialDto("A2", "P-A2")]);
        await documents.DeleteAsync(draft.Id!.Value, ct);
        await PostAsync("FTM", warehouse1, warehouse2, 1, "A3");
        (await HolderAsync("A3")).Should().Be("Magazina 2");
        var elsewhere = async () => await PostAsync("FSHM", warehouse1, buyer, 1, "A3");
        (await elsewhere.Should().ThrowAsync<ValidationException>()).Which.Errors.Values.Should().Contain(m => m.Contains("held elsewhere"));
        context.ChangeTracker.Clear();
        var nowhere = async () => await PostAsync("FSHM", warehouse1, buyer, 1, "NONE");
        (await nowhere.Should().ThrowAsync<ValidationException>()).Which.Errors.Values.Should().Contain(m => m.Contains("is not here"));
        context.ChangeTracker.Clear();
        var noNumbers = async () => await PostAsync("FSHM", warehouse1, buyer, 2);
        (await noNumbers.Should().ThrowAsync<ValidationException>()).Which.Errors.Values.Should().Contain(m => m.Contains("point of sale can't"));
        context.ChangeTracker.Clear();

        var sale = await PostAsync("FSHM", warehouse1, buyer, 2, "A1", "A2");
        (await HolderAsync("A1")).Should().Be("Bar Lura");
        var soldAgain = async () => await PostAsync("FSHM", warehouse1, buyer, 1, "A1");
        await soldAgain.Should().ThrowAsync<ValidationException>("sold once");
        context.ChangeTracker.Clear();

        // A partial reversal names what comes back; the rest follows with the whole reversal.
        var line = sale.Transactions[0].Id!.Value;
        var unnamed = async () => await documents.ReverseAsync(sale.Id!.Value, new Dictionary<Guid, decimal> { [line] = 1 }, new DateOnly(2026, 10, 5), ct);
        await unnamed.Should().ThrowAsync<ValidationException>();
        context.ChangeTracker.Clear();
        await documents.ReverseAsync(sale.Id!.Value, new Dictionary<Guid, decimal> { [line] = 1 }, new Dictionary<Guid, IReadOnlyList<string>> { [line] = ["A2"] }, new DateOnly(2026, 10, 5), ct);
        (await HolderAsync("A2"), await HolderAsync("A1")).Should().Be(("Magazina 1", "Bar Lura"));
        await documents.ReverseAsync(sale.Id!.Value, null, new DateOnly(2026, 10, 6), ct);
        (await HolderAsync("A1")).Should().Be("Magazina 1");
        var returned = async () => await documents.ReverseAsync(sale.Id!.Value, null, new DateOnly(2026, 10, 7), ct);
        await returned.Should().ThrowAsync<ValidationException>("nothing is left to reverse");
        context.ChangeTracker.Clear();

        // A number's story: every move it made, and it sells again.
        await PostAsync("FSHM", warehouse1, buyer, 1, "A2");
        var a2 = (await serials.SerialsAsync(router.Id, null, "A2", "en", ct)).Single();
        (await serials.MovesAsync(a2.Id, "en", ct)).Select(m => m.To).Should().Equal(["Magazina 1", "Bar Lura", "Bar Lura", "Magazina 1"], "by posting date: the second sale is dated before the reversal");
        (a2.Holder, a2.PartNumber).Should().Be(("Bar Lura", "P-A2"));
    }
}

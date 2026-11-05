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
using Gamma.Erp.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>A product's units and its type's (the founder, 2026-10-31): in order, the first the default; the type's when it has none; one in use stays.</summary>
public sealed class ProductUnitTests : IAsyncLifetime
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
    public async Task A_product_offers_its_own_units_in_order_or_else_its_types_and_a_unit_in_use_cannot_be_removed()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Tregtia", "AL", [("cost_center_type:WAREHOUSE", "WH", "Magazina")]);
        OrganisationIds company;
        Guid admin, clerk;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            db.Add(staff);
            await db.SaveChangesAsync(ct);
            await Organisation.CompleteCostCentersAsync(db, company.Corporate, ct);
            var (a, c) = (new User { TypeId = staff.Id, Code = "ADMIN", Name = TranslatedText.English("Admin"), TenantId = tenantId },
                new User { TypeId = staff.Id, Code = "CLERK", Name = TranslatedText.English("Clerk"), TenantId = tenantId });
            db.AddRange(a, c);
            await db.SaveChangesAsync(ct);
            (admin, clerk) = (a.Id, c.Id);
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, admin);
        var request = new RequestContext(tenantId, admin, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var visibility = new Visibility(context, request);
        var units = new ProductUnitService(context, request, visibility);
        var documents = new DocumentService(context, request, visibility, TimeProvider.System);
        var all = await context.Set<Unit>().OrderBy(u => u.Code).Take(3).ToListAsync(ct);
        var (piece, kg, box) = (all[0].Id, all[1].Id, all[2].Id);
        var goods = await context.Set<ProductCategory>().SingleAsync(c => c.Code == "PRODUCT", ct);
        var goodsType = await context.Set<ProductType>().SingleAsync(t => t.Code == "GOODS", ct);
        var beer = new Product { TypeId = goodsType.Id, Code = "BEER", Name = TranslatedText.English("Birrë"), TenantId = tenantId, OwnerId = company.Corporate };
        var supplier = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "SME", ct)).Id, Code = "S1", Name = TranslatedText.English("Furnitori"),
            TenantId = tenantId, OwnerId = company.Corporate };
        context.AddRange(beer, supplier, new InterProductCategory { ElementId = beer.Id, CategoryId = goods.Id, TenantId = tenantId, OwnerId = company.Corporate });
        await context.SaveChangesAsync(ct);

        // The type's units, in order; a product with none of its own is offered them.
        (await units.SaveTypeAsync(goodsType.Id, [piece, kg], "en", ct)).Select(u => u.UnitId).Should().Equal(piece, kg);
        var shown = (await units.ProductAsync(beer.Id, "en", ct))!;
        shown.Units.Should().BeEmpty();
        shown.TypeUnits.Select(u => u.UnitId).Should().Equal(piece, kg);
        shown.CanWrite.Should().BeTrue();
        (await units.OfferedAsync(beer.Id, ct)).Should().Equal(piece, kg);

        // Its own units, the first the default: a purchase line proposes them in that order.
        (await units.SaveProductAsync(beer.Id, [box, piece], "en", ct)).Units.Select(u => u.UnitId).Should().Equal(box, piece);
        var purchase = (await context.Set<DocumentType>().ToListAsync(ct)).Single(t => t.Code == "FBM");
        var proposal = await documents.LineProposalAsync(purchase.Id, beer.Id, supplier.Id, null, null, "en", ct);
        proposal.Units.Select(u => u.Id).Should().Equal(box, piece);
        proposal.UnitId.Should().Be(box, "the first unit is the default");
        (await units.SaveProductAsync(beer.Id, [piece, box], "en", ct)).Units.Select(u => u.UnitId).Should().Equal(piece, box);

        // Bought in pieces: pieces are in use and stay; boxes are not and go.
        var purchases = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == purchase.Id).Select(l => l.CategoryId).FirstAsync(ct);
        var item = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ITEM", ct)).Id;
        var vat = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id;
        var warehouse = await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH", ct);
        await documents.PostAsync((await documents.SaveAsync(new DocumentDto
        {
            TypeId = purchase.Id, CategoryIds = [purchases], PostingDate = new DateOnly(2026, 10, 31), FromId = supplier.Id, ToId = warehouse.Id,
            Transactions = [new TransactionDto { TypeId = item, ElementId = beer.Id, ElementName = "Birrë", UnitId = piece, Quantity = 10, Price = 1, VatGroupId = vat }],
        }, ct)).Id!.Value, ct);
        (await units.ProductAsync(beer.Id, "en", ct))!.Units.Single(u => u.UnitId == piece).InUse.Should().BeTrue();
        var dropUsed = async () => await units.SaveProductAsync(beer.Id, [box], "en", ct);
        (await dropUsed.Should().ThrowAsync<ValidationException>()).Which.Errors["unitIds"].Should().Contain("In use");
        (await units.SaveProductAsync(beer.Id, [piece], "en", ct)).Units.Select(u => u.UnitId).Should().Equal(piece);
        var unknown = async () => await units.SaveProductAsync(beer.Id, [piece, Guid.NewGuid()], "en", ct);
        await unknown.Should().ThrowAsync<ValidationException>();

        // Without the right to write products, the clerk only reads them; a type's units are an administrator's.
        var asClerk = new RequestContext(tenantId, clerk, company.Corporate, new HashSet<Guid>());
        var clerkUnits = new ProductUnitService(context, asClerk, new Visibility(context, asClerk));
        (await clerkUnits.ProductAsync(beer.Id, "en", ct))!.CanWrite.Should().BeFalse();
        var clerkSaves = async () => await clerkUnits.SaveProductAsync(beer.Id, [piece, kg], "en", ct);
        (await clerkSaves.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("rights");
        var clerkTypes = async () => await clerkUnits.SaveTypeAsync(goodsType.Id, [kg], "en", ct);
        (await clerkTypes.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("rights");
    }
}

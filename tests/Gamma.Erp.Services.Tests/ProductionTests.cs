using System.Text.Json.Nodes;
using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
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
using Gamma.Erp.Services.Configuration;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>Recipes and production (H10): a sold composite product gives up its ingredients; production runs from the recipe or from orders, at value.</summary>
public sealed class ProductionTests : IAsyncLifetime
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
    public async Task A_sold_composite_gives_up_its_ingredients_and_production_runs_from_the_recipe_or_an_order_at_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Kafe Lura", "AL", [("cost_center_type:POS_RESTAURANT", "BAR", "Bar"), ("cost_center_type:POS_RESTAURANT", "KITCHEN", "Kuzhina")]);
        OrganisationIds company;
        Guid user;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            db.Add(staff);
            await db.SaveChangesAsync(ct);
            await Organisation.CompleteCostCentersAsync(db, company.Corporate, ct);
            var cook = new User { TypeId = staff.Id, Code = "COOK", Name = TranslatedText.English("Cook"), TenantId = tenantId };
            db.Add(cook);
            await db.SaveChangesAsync(ct);
            user = cook.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var visibility = new Visibility(context, request);
        var centres = await context.Set<CostCenter>().ToDictionaryAsync(c => c.Code!, c => c.Id, ct);
        var units = await context.Set<Unit>().ToDictionaryAsync(u => u.Code!, u => u.Id, ct);
        var goods = await context.Set<ProductCategory>().SingleAsync(c => c.Code == "PRODUCT", ct);
        var goodsType = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == goods.Id).Select(l => l.TypeId).FirstAsync(ct);
        Product Make(string code, string name, bool composite = false)
        {
            var p = new Product { TypeId = goodsType, Code = code, Name = TranslatedText.English(name), IsComposite = composite, TenantId = tenantId, OwnerId = company.Corporate };
            context.AddRange(p, new InterProductCategory { ElementId = p.Id, CategoryId = goods.Id, TenantId = tenantId, OwnerId = company.Corporate });
            return p;
        }

        var coffee = Make("COFFEE", "Kafe kokërr");
        var milk = Make("MILK", "Qumësht");
        var flour = Make("FLOUR", "Miell");
        var eggs = Make("EGGS", "Vezë");
        var macchiato = Make("MACCHIATO", "Makiato", composite: true);
        var cake = Make("CAKE", "Ëmbëlsirë");
        await context.SaveChangesAsync(ct);

        // What the places hold comes in through purchase bills (the founder's rule): coffee at 2 a gram, milk at 0.5, flour at 8 a kilo, eggs at 2.
        var documents = new DocumentService(context, request, visibility, TimeProvider.System);
        var types = await context.Set<DocumentType>().ToListAsync(ct);
        var item = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ITEM", ct)).Id;
        var vat = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id;
        var vat0 = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 0, ct)).Id;
        var supplier = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id, Code = "S1", Name = TranslatedText.English("Furnitori"),
            TenantId = tenantId, OwnerId = company.Corporate };
        context.Add(supplier);
        await context.SaveChangesAsync(ct);
        async Task Buy(string place, Product p, string unit, decimal quantity, decimal price)
        {
            var type = types.Single(t => t.Code == "FBM");
            var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == type.Id).Select(l => l.CategoryId).FirstAsync(ct);
            var saved = await documents.SaveAsync(new DocumentDto
            {
                TypeId = type.Id, CategoryIds = [category], DocumentDate = new DateOnly(2026, 10, 29), PostingDate = new DateOnly(2026, 10, 29), FromId = supplier.Id,
                FromName = "Furnitori", ToId = centres[place], ToName = place,
                Transactions = [new TransactionDto { TypeId = item, ElementId = p.Id, ElementTypeId = p.TypeId, ElementName = p.Code, UnitId = units[unit], Quantity = quantity,
                    Price = price, VatGroupId = vat0 }],
            }, ct);
            await documents.PostAsync(saved.Id!.Value, ct);
            context.ChangeTracker.Clear();
        }

        await Buy("BAR", coffee, "G", 1000, 2);
        await Buy("BAR", milk, "G", 1000, 0.5m);
        await Buy("KITCHEN", flour, "KG", 10, 8);
        await Buy("KITCHEN", eggs, "PIECE", 30, 2);
        async Task<decimal> Value(Product p) => await (from b in context.Set<ProductBalance>() join u in context.Set<InterProductSpecificUnit>() on b.ProductSpecificUnitId equals u.Id
                                                       where u.ProductId == p.Id select (b.DebitLcu ?? 0) - (b.CreditLcu ?? 0)).SumAsync(ct);

        // Recipes are configuration rows: a macchiato is 8 g of coffee and 20 g of milk (10 % lost); a cake 0.5 kg of flour (10 % lost) and 3 eggs.
        var config = new ConfigurationService(context, request, visibility);
        async Task Recipe(Product p, Product component, string unit, decimal quantity, decimal waste = 0) => await config.SaveAsync("product-recipes", new JsonObject
        {
            ["productId"] = p.Id.ToString(), ["unitId"] = units["PIECE"].ToString(), ["componentId"] = component.Id.ToString(), ["componentUnitId"] = units[unit].ToString(),
            ["quantity"] = quantity, ["wastePercent"] = waste,
        }, ct);
        await Recipe(macchiato, coffee, "G", 8);
        await Recipe(macchiato, milk, "G", 20, 10);
        await Recipe(cake, flour, "KG", 0.5m, 10);
        await Recipe(cake, eggs, "PIECE", 3);
        var itself = async () => await Recipe(cake, cake, "PIECE", 1);
        (await itself.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("quantity");

        // Two macchiatos sold on a point-of-sale bill: the bar gives up 16 g of coffee and 44 g of milk, no macchiato, and their value at cost.
        var bill = types.Single(t => t.Code == "PSH");
        var billCategory = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == bill.Id).Select(l => l.CategoryId).FirstAsync(ct);
        var walkIn = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "UNKNOWN", ct)).Id, Code = "WALKIN", Name = TranslatedText.English("Klient"),
            TenantId = tenantId, OwnerId = company.Corporate };
        context.AddRange(walkIn, new ProductSalePrice { CompanyId = company.Corporate, ProductId = macchiato.Id, Value = 120, ValueLcu = 120, TenantId = tenantId, OwnerId = company.Corporate });
        await context.SaveChangesAsync(ct);
        await documents.PostAsync((await documents.SaveAsync(new DocumentDto
        {
            TypeId = bill.Id, CategoryIds = [billCategory], DocumentDate = new DateOnly(2026, 10, 30), PostingDate = new DateOnly(2026, 10, 30), ToId = walkIn.Id, ToName = "Klient",
            Transactions = [new TransactionDto { TypeId = item, ElementId = macchiato.Id, ElementTypeId = macchiato.TypeId, ElementName = "Makiato", UnitId = units["PIECE"], Quantity = 2,
                Price = 120, VatGroupId = vat, FromId = centres["BAR"], FromName = "Bar" }],
        }, ct)).Id!.Value, ct);
        context.ChangeTracker.Clear();
        async Task<decimal> Held(string place, Product p, string unit) => await (from s in context.Set<InterCostCenterProductStock>()
                                                                                    join u in context.Set<InterProductSpecificUnit>() on s.ProductSpecificUnitId equals u.Id
                                                                                    where s.CostCenterId == centres[place] && u.ProductId == p.Id && u.UnitId == units[unit]
                                                                                    select s.Quantity ?? 0).SingleOrDefaultAsync(ct);
        (await Held("BAR", coffee, "G")).Should().Be(984);
        (await Held("BAR", milk, "G")).Should().Be(956);
        (await Held("BAR", macchiato, "PIECE")).Should().Be(0, "a composite is not held, its ingredients are");
        ((await Value(coffee)), (await Value(milk)), (await Value(macchiato))).Should().Be((1968m, 478m, 0m), "the ingredients' value goes, at what they cost");

        // Four cakes: calculated from the recipe, then made in the kitchen by a production document, at what the ingredients cost; the recipe's
        // 10 % of flour lost is a waste (firo) line, an expense, not part of the cakes' cost.
        var production = new ProductionService(context, request, visibility, TimeProvider.System);
        var calculated = await production.CalculateAsync(cake.Id, units["PIECE"], 4, centres["KITCHEN"], ct);
        calculated.Select(l => (l.ProductId, l.Quantity, l.WasteQuantity, l.Held)).Should().Equal((flour.Id, 2.2m, 0.2m, 10m), (eggs.Id, 12m, 0m, 30m));
        var run = await production.ProduceAsync(new ProduceRequest(centres["KITCHEN"], cake.Id, units["PIECE"], 4, new DateOnly(2026, 10, 30)), ct);
        context.ChangeTracker.Clear();
        run.Code.Should().StartWith("FPR", "Albania's production documents");
        run.Transactions.Select(t => (t.ElementId, t.Quantity, t.CostLcu)).Should().Equal((flour.Id, 2m, 16m), (eggs.Id, 12m, 24m), (flour.Id, 0.2m, 1.6m), (cake.Id, 4m, 40m));
        (await Held("KITCHEN", cake, "PIECE")).Should().Be(4);
        (await Held("KITCHEN", flour, "KG")).Should().Be(7.8m);
        (await Value(cake)).Should().Be(40m, "the cakes came in at what went into them");
        var entries = await context.Set<ChartValue>().Where(e => e.DocumentId == run.Id).ToListAsync(ct);
        (entries.Sum(e => e.Debit ?? 0), entries.Sum(e => e.Credit ?? 0)).Should().Be((81.6m, 81.6m),
            "raw materials off stock onto production, the waste onto its expense, the cakes from production onto stock");
        var waste687 = await context.Set<Chart>().Where(c => c.Code == "687").Select(c => c.Id).ToListAsync(ct);
        entries.Where(e => waste687.Contains(e.ChartId!.Value)).Sum(e => (e.Debit ?? 0) - (e.Credit ?? 0)).Should().Be(1.6m, "Albania's waste expense account");
        var production331 = await context.Set<Chart>().Where(c => c.Code == "331").Select(c => c.Id).ToListAsync(ct);
        entries.Where(e => production331.Contains(e.ChartId!.Value)).Sum(e => (e.Debit ?? 0) - (e.Credit ?? 0)).Should().Be(0, "the production account nets to 0");

        // As actually used, and refused when the company has too little.
        var used = await production.ProduceAsync(new ProduceRequest(centres["KITCHEN"], cake.Id, units["PIECE"], 1,
            Lines: [new ProductionLineDto(flour.Id, null, units["KG"], 0.6m, 0.1m), new ProductionLineDto(eggs.Id, null, units["PIECE"], 3, 0)]), ct);
        context.ChangeTracker.Clear();
        used.Transactions.Single(t => t.ElementId == cake.Id).CostLcu.Should().Be(10m, "0.5 kg of flour and 3 eggs; the 0.1 kg lost is waste");
        var tooMany = async () => await production.ProduceAsync(new ProduceRequest(centres["KITCHEN"], cake.Id, units["PIECE"], 100), ct);
        (await tooMany.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.StockError);
        context.ChangeTracker.Clear();

        // A production is reversed like any document: the cakes go, the flour and eggs come back at what they cost.
        await documents.ReverseAsync(run.Id!.Value, null, new DateOnly(2026, 10, 30), ct);
        context.ChangeTracker.Clear();
        (await Held("KITCHEN", cake, "PIECE")).Should().Be(1);
        (await Held("KITCHEN", flour, "KG")).Should().Be(9.4m);
        ((await Value(cake)), (await Value(flour))).Should().Be((10m, 75.2m));

        // A customer's order for two cakes: listed until it is produced, then produced, linked to it.
        var orderType = types.Single(t => t.Code == "OSHM");
        var orderCategory = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == orderType.Id).Select(l => l.CategoryId).FirstAsync(ct);
        var hotel = new Partner { TypeId = supplier.TypeId, Code = "C1", Name = TranslatedText.English("Hotel Lura"), TenantId = tenantId, OwnerId = company.Corporate };
        context.Add(hotel);
        await context.SaveChangesAsync(ct);
        var order = await documents.SaveAsync(new DocumentDto
        {
            TypeId = orderType.Id, CategoryIds = [orderCategory], DocumentDate = new DateOnly(2026, 10, 30), FromId = centres["KITCHEN"], FromName = "Kuzhina", ToId = hotel.Id,
            ToName = "Hotel Lura",
            Transactions = [new TransactionDto { TypeId = item, ElementId = cake.Id, ElementTypeId = cake.TypeId, ElementName = "Ëmbëlsirë", UnitId = units["PIECE"], Quantity = 2,
                Price = 10, VatGroupId = vat }],
        }, ct);
        context.ChangeTracker.Clear();
        var open = await production.OrdersToProduceAsync(orderType.Id, ct);
        open.Should().ContainSingle(o => o.Id == order.Id!.Value).Which.Products.Should().ContainSingle(p => p.ProductId == cake.Id && p.Quantity == 2);
        var made = await production.ProduceOrderAsync(order.Id!.Value, centres["KITCHEN"], new DateOnly(2026, 10, 30), ct);
        context.ChangeTracker.Clear();
        made.Should().ContainSingle().Which.CreatedFromIds.Should().Equal(order.Id!.Value);
        (await production.OrdersToProduceAsync(orderType.Id, ct)).Should().NotContain(o => o.Id == order.Id!.Value);
        (await Held("KITCHEN", cake, "PIECE")).Should().Be(3);
    }
}

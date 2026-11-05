using System.Text.Json.Nodes;
using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Configuration;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

using Products = EntityService<ProductCategory, ProductType, InterProductTypeCategory, ProductTypeInfo, ProductTypeInfoChoice, Product, ProductInfo,
    InterProductCategory, ProductAccess>;

/// <summary>Units and properties (H8): unit conversions, splitting stock into the sale unit, multi-text and activating-list properties, copying properties.</summary>
public sealed class UnitsAndPropertiesTests : IAsyncLifetime
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

    private async Task<(ErpDbContext Db, RequestContext Request, Visibility Visibility, OrganisationIds Company, Dictionary<string, Guid> Centres)> CompanyAsync(CancellationToken ct)
    {
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Kafe Lura", "AL", [("cost_center_type:POS_RESTAURANT", "BAR", "Bar")]);
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
            var owner = new User { TypeId = staff.Id, Code = "OWNER", Name = TranslatedText.English("Owner"), TenantId = tenantId };
            db.Add(owner);
            await db.SaveChangesAsync(ct);
            user = owner.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        var context = Context(tenant);
        return (context, request, new Visibility(context, request), company, await context.Set<CostCenter>().ToDictionaryAsync(c => c.Code!, c => c.Id, ct));
    }

    [Fact]
    public async Task Conversions_work_both_ways_a_product_has_its_own_and_stock_is_split_into_the_sale_unit_with_its_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, request, visibility, company, centres) = await CompanyAsync(ct);
        await using var _ = db;
        var units = await db.Set<Unit>().ToDictionaryAsync(u => u.Code!, u => u.Id, ct);
        var service = new UnitConversionService(db, request, visibility, TimeProvider.System);
        var goods = await db.Set<ProductCategory>().SingleAsync(c => c.Code == "PRODUCT", ct);
        var goodsType = await db.Set<InterProductTypeCategory>().Where(l => l.CategoryId == goods.Id).Select(l => l.TypeId).FirstAsync(ct);
        var coffee = new Product { TypeId = goodsType, Code = "COFFEE", Name = TranslatedText.English("Kafe"), TenantId = request.TenantId, OwnerId = company.Corporate };
        db.AddRange(coffee, new InterProductCategory { ElementId = coffee.Id, CategoryId = goods.Id, TenantId = request.TenantId, OwnerId = company.Corporate });
        await db.SaveChangesAsync(ct);

        // The pack's conversions, either way round; a product's own one wins and is only its own.
        (await service.FactorAsync(units["KG"], units["G"], null, ct)).Should().Be(1000);
        (await service.FactorAsync(units["G"], units["KG"], null, ct)).Should().Be(0.001m);
        (await service.FactorAsync(units["PIECE"], units["G"], coffee.Id, ct)).Should().BeNull();
        var config = new ConfigurationService(db, request, visibility);
        await config.SaveAsync("unit-conversions", new JsonObject
        {
            ["fromUnitId"] = units["PIECE"].ToString(), ["toUnitId"] = units["G"].ToString(), ["factor"] = 250, ["productId"] = coffee.Id.ToString(),
        }, ct);
        (await service.FactorAsync(units["PIECE"], units["G"], coffee.Id, ct)).Should().Be(250, "a pack of this coffee is 250 g");
        (await service.FactorAsync(units["PIECE"], units["G"], null, ct)).Should().BeNull();
        var zero = async () => await config.SaveAsync("unit-conversions", new JsonObject
        {
            ["fromUnitId"] = units["KG"].ToString(), ["toUnitId"] = units["G"].ToString(), ["factor"] = 0,
        }, ct);
        (await zero.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("factor");

        // 3 kg of coffee bought for the bar: 1 kg at 10, then 2 kg at 12.
        var documents = new Services.Documents.DocumentService(db, request, visibility, TimeProvider.System);
        var types = await db.Set<Models.Documents.DocumentType>().ToListAsync(ct);
        var item = (await db.Set<Models.Transactions.TransactionType>().SingleAsync(t => t.Code == "ITEM", ct)).Id;
        var vat0 = (await db.Set<Models.Payments.VatGroup>().SingleAsync(v => v.Rate == 0, ct)).Id;
        var supplier = new Models.Partners.Partner { TypeId = (await db.Set<Models.Partners.PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id, Code = "S1",
            Name = TranslatedText.English("Furnitori"), TenantId = request.TenantId, OwnerId = company.Corporate };
        db.Add(supplier);
        await db.SaveChangesAsync(ct);
        foreach (var (quantity, price, day) in new[] { (1m, 10m, 28), (2m, 12m, 29) })
        {
            var type = types.Single(t => t.Code == "FBM");
            var category = await db.Set<Models.Documents.InterDocumentTypeCategory>().Where(l => l.TypeId == type.Id).Select(l => l.CategoryId).FirstAsync(ct);
            var saved = await documents.SaveAsync(new Models.Documents.Dto.DocumentDto
            {
                TypeId = type.Id, CategoryIds = [category], DocumentDate = new DateOnly(2026, 10, day), PostingDate = new DateOnly(2026, 10, day), FromId = supplier.Id,
                FromName = "Furnitori", ToId = centres["BAR"], ToName = "Bar",
                Transactions = [new Models.Transactions.Dto.TransactionDto { TypeId = item, ElementId = coffee.Id, ElementTypeId = coffee.TypeId, ElementName = "Kafe",
                    UnitId = units["KG"], Quantity = quantity, Price = price, VatGroupId = vat0 }],
            }, ct);
            await documents.PostAsync(saved.Id!.Value, ct);
            db.ChangeTracker.Clear();
        }

        // Split into grams by a unit split document: each layer on its own, its unit cost changed by the same factor (1 kg at 10 → 1000 g at 0.010).
        var split = await service.ConvertAsync(new ConvertStockRequest(centres["BAR"], coffee.Id, units["KG"], units["G"], 3, new DateOnly(2026, 10, 30)), ct);
        db.ChangeTracker.Clear();
        split.Code.Should().StartWith("FNJ", "Albania's unit split documents");
        split.Transactions.Select(t => (t.UnitId, t.Quantity, t.CostLcu)).Should().Equal((units["KG"], 3m, 34m), (units["G"], 3000m, 34m));
        var layers = await (from l in db.Set<ProductCostLayer>() join u in db.Set<InterProductSpecificUnit>() on l.ProductSpecificUnitId equals u.Id
                            where u.ProductId == coffee.Id && u.UnitId == units["G"] orderby l.LayerDate select new { l.LayerDate, l.QuantityLeft, l.ValueLeftLcu, l.UnitCostLcu })
            .ToListAsync(ct);
        layers.Select(l => (l.LayerDate.Day, l.QuantityLeft, l.ValueLeftLcu, l.UnitCostLcu)).Should().Equal((28, 1000m, 10m, 0.01m), (29, 2000m, 24m, 0.012m));

        var stock = await (from s in db.Set<InterCostCenterProductStock>() join u in db.Set<InterProductSpecificUnit>() on s.ProductSpecificUnitId equals u.Id
                           where s.CostCenterId == centres["BAR"] && u.ProductId == coffee.Id select new { u.UnitId, u.Id, s.Quantity }).ToListAsync(ct);
        stock.Single(s => s.UnitId == units["KG"]).Quantity.Should().Be(0);
        stock.Single(s => s.UnitId == units["G"]).Quantity.Should().Be(3000);
        var (kg, grams) = (stock.Single(s => s.UnitId == units["KG"]).Id, stock.Single(s => s.UnitId == units["G"]).Id);
        (await db.Set<ProductBalance>().Where(b => b.ProductSpecificUnitId == kg).SumAsync(b => b.DebitLcu - b.CreditLcu, ct)).Should().Be(0, "all the value moved");
        (await db.Set<ProductBalance>().Where(b => b.ProductSpecificUnitId == grams).SumAsync(b => b.DebitLcu - b.CreditLcu, ct)).Should().Be(34, "nothing gained or lost");

        var more = async () => await service.ConvertAsync(new ConvertStockRequest(centres["BAR"], coffee.Id, units["KG"], units["G"], 1), ct);
        (await more.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.StockError);
        db.ChangeTracker.Clear();
        var none = async () => await service.ConvertAsync(new ConvertStockRequest(centres["BAR"], coffee.Id, units["G"], units["HOUR"], 1), ct);
        (await none.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.NotConfiguredError);
    }

    [Fact]
    public async Task An_activating_list_shows_its_properties_multi_text_keeps_several_texts_and_properties_are_copied_with_their_links()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, request, visibility, _, _) = await CompanyAsync(ct);
        await using var _ = db;
        var products = new Products(db, request, visibility);
        var goods = (await products.CategoriesAsync(ct)).Single(c => c.Code == "PRODUCT");
        static Dictionary<string, string> En(string text) => new() { ["en"] = text };

        // A list and the properties it shows are saved in two steps: a new choice has its id once saved.
        var fridge = await products.SaveTypeAsync(new ElementTypeDto
        {
            Name = En("Fridge"), IsElement = true, CategoryIds = [goods.Id!.Value],
            Properties =
            [
                new TypeInfoDto { Name = En("Warranty"), Kind = PropertyKind.ActivatingList, Choices = [new TypeInfoChoiceDto { Name = En("Yes") }, new TypeInfoChoiceDto { Name = En("No") }] },
                new TypeInfoDto { Name = En("Phones"), Kind = PropertyKind.MultiText },
                new TypeInfoDto { Name = En("Colour"), Kind = PropertyKind.List, Choices = [new TypeInfoChoiceDto { Name = En("White") }] },
            ],
        }, ct);
        var warranty = fridge.Properties.Single(p => p.Name["en"] == "Warranty");
        var yes = warranty.Choices.Single(c => c.Name["en"] == "Yes").Id!.Value;
        var white = fridge.Properties.Single(p => p.Name["en"] == "Colour").Choices.Single().Id!.Value;
        var badLink = async () => await products.SaveTypeAsync(fridge with
        {
            Properties = [.. fridge.Properties, new TypeInfoDto { Name = En("Shade"), Kind = PropertyKind.Text, ShownForChoiceId = white }],
        }, ct);
        (await badLink.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("properties[3].shownForChoiceId", "a plain list shows nothing");
        fridge = await products.SaveTypeAsync(fridge with
        {
            Properties = [.. fridge.Properties, new TypeInfoDto { Name = En("Warranty months"), Kind = PropertyKind.Number, Required = true, ShownForChoiceId = yes }],
        }, ct);
        var months = fridge.Properties.Single(p => p.Name["en"] == "Warranty months");
        months.ShownForChoiceId.Should().Be(yes);
        var phones = fridge.Properties.Single(p => p.Name["en"] == "Phones").Id!.Value;

        ElementDto Element(Guid? pick, JsonNode? monthsValue) => new()
        {
            TypeId = fridge.Id!.Value, Name = En($"Fridge {Guid.NewGuid():N}"), CategoryIds = [goods.Id!.Value],
            Values = new()
            {
                [warranty.Id!.Value] = pick is null ? null : JsonValue.Create(pick.Value.ToString()), [months.Id!.Value] = monthsValue,
                [phones] = new JsonArray("069 111", new JsonObject { ["en"] = "069 222" }, " "),
            },
        };

        // Shown and required while "Yes" is picked; with "No" it is neither required nor kept.
        var missing = async () => await products.SaveElementAsync(Element(yes, null), ct);
        (await missing.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey($"values.{months.Id}");
        var withWarranty = await products.SaveElementAsync(Element(yes, 24), ct);
        withWarranty.Values[warranty.Id!.Value]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal(yes.ToString());
        withWarranty.Values[months.Id!.Value]!.GetValue<decimal>().Should().Be(24);
        withWarranty.Values[phones]!.AsArray().Select(n => n!["en"]!.GetValue<string>()).Should().Equal("069 111", "069 222");
        var no = warranty.Choices.Single(c => c.Name["en"] == "No").Id!.Value;
        var without = await products.SaveElementAsync(Element(no, 24), ct);
        without.Values.Should().NotContainKey(months.Id!.Value);

        // Copying onto another type: the properties, their choices, and the list's link to its new choice; again, nothing more.
        var tv = await products.SaveTypeAsync(new ElementTypeDto
        {
            Name = En("TV"), IsElement = true, CategoryIds = [goods.Id!.Value], Properties = [new TypeInfoDto { Name = En("Colour"), Kind = PropertyKind.Text }],
        }, ct);
        tv = await products.CopyPropertiesAsync(tv.Id!.Value, fridge.Id!.Value, null, ct);
        tv.Properties.Select(p => p.Name["en"]).Should().Equal("Colour", "Warranty", "Phones", "Warranty months");
        tv.Properties.Single(p => p.Name["en"] == "Colour").Kind.Should().Be(PropertyKind.Text, "a property the type has is left as it is");
        var tvYes = tv.Properties.Single(p => p.Name["en"] == "Warranty").Choices.Single(c => c.Name["en"] == "Yes").Id;
        tvYes.Should().NotBe(yes);
        tv.Properties.Single(p => p.Name["en"] == "Warranty months").ShownForChoiceId.Should().Be(tvYes);
        (await products.CopyPropertiesAsync(tv.Id!.Value, fridge.Id!.Value, null, ct)).Properties.Should().HaveCount(4);
        var foreign = async () => await products.CopyPropertiesAsync(tv.Id!.Value, fridge.Id!.Value, [Guid.NewGuid()], ct);
        await foreign.Should().ThrowAsync<ValidationException>();
    }
}

using AwesomeAssertions;
using System.Text.Json.Nodes;
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
using Gamma.Erp.Services.Pos;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>The point of sale (H7): menus by the time of day, bills per table and waiter with their payment method, paying a table's or all bills, stock.</summary>
public sealed class PosTests : IAsyncLifetime
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
    public async Task Menus_open_by_the_time_bills_are_kept_per_table_and_waiter_and_paid_by_table_or_all_and_the_bar_gives_up_its_stock()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Bar Lura", "AL", [("cost_center_type:POS_RESTAURANT", "BAR", "Bar"), ("cost_center_type:TABLE", "T1", "Tavolina 1"),
            ("cost_center_type:TABLE", "T2", "Tavolina 2"), ("cost_center_type:CASH_DESK", "CASH", "Arka")]);
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
            var waiter = new User { TypeId = staff.Id, Code = "WAITER", Name = TranslatedText.English("Waiter"), TenantId = tenantId };
            db.Add(waiter);
            await db.SaveChangesAsync(ct);
            user = waiter.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var visibility = new Visibility(context, request);
        var documents = new DocumentService(context, request, visibility, TimeProvider.System);
        var pos = new PosService(context, visibility, documents, TimeProvider.System);
        var centres = await context.Set<CostCenter>().ToDictionaryAsync(c => c.Code!, c => c.Id, ct);
        var walkIn = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "UNKNOWN", ct)).Id, Code = "WALKIN", Name = TranslatedText.English("Klient"),
            TenantId = tenantId, OwnerId = company.Corporate };
        var goods = await context.Set<ProductCategory>().SingleAsync(c => c.Code == "PRODUCT", ct);
        var goodsType = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == goods.Id).Select(l => l.TypeId).FirstAsync(ct);
        var beer = new Product { TypeId = goodsType, Code = "BEER", Name = TranslatedText.English("Birrë"), TenantId = tenantId, OwnerId = company.Corporate };
        var morning = new SaleMenu { Code = "AM", Name = TranslatedText.English("Mëngjes"), StartsAt = new TimeOnly(6, 0), EndsAt = new TimeOnly(12, 0), TenantId = tenantId,
            OwnerId = company.Corporate };
        var night = new SaleMenu { Code = "PM", Name = TranslatedText.English("Natë"), StartsAt = new TimeOnly(18, 0), EndsAt = new TimeOnly(2, 0), TenantId = tenantId,
            OwnerId = company.Corporate };
        context.AddRange(walkIn, beer, morning, night, new InterProductCategory { ElementId = beer.Id, CategoryId = goods.Id, TenantId = tenantId, OwnerId = company.Corporate },
            new SaleMenuCategory { SaleMenuId = morning.Id, ProductCategoryId = goods.Id, TenantId = tenantId, OwnerId = company.Corporate },
            new SaleMenuCategory { SaleMenuId = night.Id, ProductCategoryId = goods.Id, TenantId = tenantId, OwnerId = company.Corporate },
            new ProductSalePrice { CompanyId = company.Corporate, ProductId = beer.Id, Value = 150, ValueLcu = 150, TenantId = tenantId, OwnerId = company.Corporate });
        await context.SaveChangesAsync(ct);

        var at9 = await pos.CurrentMenusAsync(centres["BAR"], new TimeOnly(9, 0), "en", ct);
        at9.Should().ContainSingle().Which.Code.Should().Be("AM");
        at9[0].Categories.Should().ContainSingle().Which.Products.Should().ContainSingle(p => p.ProductId == beer.Id && p.Price == 150);
        (await pos.CurrentMenusAsync(centres["BAR"], new TimeOnly(1, 0), "en", ct)).Should().ContainSingle().Which.Code.Should().Be("PM", "the night menu runs past midnight");
        (await pos.CurrentMenusAsync(centres["BAR"], new TimeOnly(15, 0), "en", ct)).Should().BeEmpty();

        var types = await context.Set<DocumentType>().ToListAsync(ct);
        var bill = types.Single(t => t.Code == "PSH");
        var moneyIn = types.Single(t => t.Name.In("en") == "Point-of-sale receipt of money");
        var billCategory = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == bill.Id).Select(l => l.CategoryId).FirstAsync(ct);
        var methods = await context.Set<PaymentMethod>().ToListAsync(ct);
        var cash = methods[0].Id;
        var unit = (await context.Set<Unit>().FirstAsync(ct)).Id;
        var item = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ITEM", ct)).Id;
        var vat = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id;
        async Task<DocumentDto> BillAsync(string table, decimal quantity, Guid? method = null) => await documents.PostAsync((await documents.SaveAsync(new DocumentDto
        {
            TypeId = bill.Id, CategoryIds = [billCategory], DocumentDate = new DateOnly(2026, 10, 30), PostingDate = new DateOnly(2026, 10, 30), ToId = walkIn.Id, ToName = "Klient",
            TableId = centres[table], PaymentMethodId = method ?? cash,
            Transactions = [new TransactionDto { TypeId = item, ElementId = beer.Id, ElementTypeId = beer.TypeId, ElementName = "Birrë", UnitId = unit, Quantity = quantity,
                Price = 150, VatGroupId = vat, FromId = centres["BAR"], FromName = "Bar" }],
        }, ct)).Id!.Value, ct);

        // The bar holds what it sells (cost layers, H12): bought into it first.
        var seller = new Partner { TypeId = walkIn.TypeId, Code = "S1", Name = TranslatedText.English("Birra Korça"), TenantId = tenantId, OwnerId = company.Corporate };
        context.Add(seller);
        await context.SaveChangesAsync(ct);
        var purchase = types.Single(t => t.Code == "FBM");
        var purchases = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == purchase.Id).Select(l => l.CategoryId).FirstAsync(ct);
        await documents.PostAsync((await documents.SaveAsync(new DocumentDto
        {
            TypeId = purchase.Id, CategoryIds = [purchases], PostingDate = new DateOnly(2026, 10, 29), FromId = seller.Id, ToId = centres["BAR"],
            Transactions = [new TransactionDto { TypeId = item, ElementId = beer.Id, ElementName = "Birrë", UnitId = unit, Quantity = 10, Price = 60, VatGroupId = vat }],
        }, ct)).Id!.Value, ct);

        var first = await BillAsync("T1", 2);
        await BillAsync("T1", 1);
        await BillAsync("T2", 3);
        first.PaymentMethodId.Should().Be(cash);
        (await pos.UnpaidAsync(bill.Id, null, centres["T1"], ct)).Select(b => b.Open).Should().Equal(360, 180);
        (await pos.UnpaidAsync(bill.Id, user, null, ct)).Should().HaveCount(3, "the waiter's bills");

        var payment = await pos.PayAsync(new PayBillsRequest(bill.Id, moneyIn.Id, centres["CASH"], TableId: centres["T1"], PaymentMethodId: cash), ct);
        payment.Should().ContainSingle().Which.Posted.Should().BeTrue();
        payment[0].Transactions.Sum(t => t.ValueVat).Should().Be(540, "both bills of table 1");
        payment[0].CreatedFromIds.Should().HaveCount(2);
        (await pos.UnpaidAsync(bill.Id, null, null, ct)).Should().ContainSingle().Which.TableId.Should().Be(centres["T2"]);
        await pos.PayAsync(new PayBillsRequest(bill.Id, moneyIn.Id, centres["CASH"], All: true), ct);
        (await pos.UnpaidAsync(bill.Id, null, null, ct)).Should().BeEmpty();
        var nothing = async () => await pos.PayAsync(new PayBillsRequest(bill.Id, moneyIn.Id, centres["CASH"], All: true), ct);
        await nothing.Should().ThrowAsync<ValidationException>();

        // The bar gave up the beer it sold (the point-of-sale bill now takes stock out of its line's place).
        var specific = await context.Set<InterProductSpecificUnit>().Where(s => s.ProductId == beer.Id).Select(s => s.Id).SingleAsync(ct);
        (await context.Set<InterCostCenterProductStock>().SingleAsync(s => s.CostCenterId == centres["BAR"] && s.ProductSpecificUnitId == specific, ct)).Quantity.Should().Be(4, "10 bought, 6 sold");

        // A payment method the document type does not take is refused.
        if (methods.Count > 1)
        {
            context.Add(new InterPaymentMethodDocumentType { DocumentTypeId = bill.Id, PaymentMethodId = cash, TenantId = tenantId, OwnerId = company.Corporate });
            await context.SaveChangesAsync(ct);
            var card = async () => await BillAsync("T1", 1, methods[1].Id);
            (await card.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("paymentMethodId");
        }
    }

    [Fact]
    public async Task A_point_of_sale_takes_its_own_settings_over_the_company_default_and_its_floor_plan_shows_the_open_bills_and_waiters()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Bar Lura", "AL", [("cost_center_type:POS_RESTAURANT", "BAR", "Bar"), ("cost_center_type:HALL", "HALL", "Salla"),
            ("cost_center_type:TABLE", "T1", "Tavolina 1"), ("cost_center_type:TABLE", "T2", "Tavolina 2"), ("cost_center_type:CASH_DESK", "CASH", "Arka")]);
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
            var waiter = new User { TypeId = staff.Id, Code = "WAITER", Name = TranslatedText.English("Arben"), TenantId = tenantId };
            db.Add(waiter);
            await db.SaveChangesAsync(ct);
            user = waiter.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var visibility = new Visibility(context, request);
        var documents = new DocumentService(context, request, visibility, TimeProvider.System);
        var pos = new PosService(context, visibility, documents, TimeProvider.System);
        var centres = await context.Set<CostCenter>().ToDictionaryAsync(c => c.Code!, ct);

        // The pack's company default names the bill and money types, never a cash desk or a client: those are the company's own.
        var fallback = await pos.SettingsAsync(null, "en", ct);
        fallback.BillTypeId.Should().NotBeNull();
        fallback.MoneyInType.Should().Be("Point-of-sale receipt of money");
        fallback.MoneyOutType.Should().Be("Point-of-sale payment");
        fallback.Missing.Should().BeEquivalentTo("cashDeskId", "walkInClientId");

        var walkIn = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "UNKNOWN", ct)).Id, Code = "WALKIN", Name = TranslatedText.English("Klient"),
            TenantId = tenantId, OwnerId = company.Corporate };
        context.AddRange(walkIn, new PointOfSaleSetting { PointOfSaleId = centres["BAR"].Id, CashDeskId = centres["CASH"].Id, WalkInClientId = walkIn.Id, TenantId = tenantId,
            OwnerId = company.Companies["CO"] });
        // The floor plan: the hall in the bar, the tables in the hall; table 1 has its place on the plan.
        centres["HALL"].IdSup = centres["BAR"].Id;
        centres["T1"].IdSup = centres["HALL"].Id;
        centres["T2"].IdSup = centres["HALL"].Id;
        var x = (await context.Set<CostCenterTypeInfo>().Where(i => i.TypeId == centres["T1"].TypeId).ToListAsync(ct)).Single(i => i.Name.In("en") == "Position X");
        context.Add(new CostCenterInfo { ElementId = centres["T1"].Id, TypeInfoId = x.Id, Value = JsonValue.Create(120), TenantId = tenantId, OwnerId = company.Companies["CO"] });
        await context.SaveChangesAsync(ct);

        var bar = await pos.SettingsAsync(centres["BAR"].Id, "en", ct);
        bar.Missing.Should().BeEmpty();
        bar.Screen.Should().Be("tables", "its kind, a restaurant, says which screen it opens");
        fallback.Screen.Should().BeNull();
        bar.CashDesk.Should().Be("Arka");
        bar.WalkInClient.Should().Be("Klient");
        bar.BillTypeId.Should().Be(fallback.BillTypeId, "an empty column of the bar's own row takes the default's");
        (await pos.SettingsAsync(null, "en", ct)).Missing.Should().HaveCount(2, "the default stays as it was");

        var billCategory = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == bar.BillTypeId).Select(l => l.CategoryId).FirstAsync(ct);
        var goods = await context.Set<ProductCategory>().SingleAsync(c => c.Code == "PRODUCT", ct);
        var goodsType = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == goods.Id).Select(l => l.TypeId).FirstAsync(ct);
        var beer = new Product { TypeId = goodsType, Code = "BEER", Name = TranslatedText.English("Birrë"), TenantId = tenantId, OwnerId = company.Corporate };
        var supplier = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "SME", ct)).Id, Code = "S1", Name = TranslatedText.English("Birra Korça"),
            TenantId = tenantId, OwnerId = company.Corporate };
        context.AddRange(beer, supplier, new InterProductCategory { ElementId = beer.Id, CategoryId = goods.Id, TenantId = tenantId, OwnerId = company.Corporate });
        await context.SaveChangesAsync(ct);
        // The founder (2026-10-31): a barcode is a property of the product; a shop reads them with their prices.
        var barcode = (await context.Set<ProductTypeInfo>().Where(i => i.TypeId == goodsType).ToListAsync(ct)).Single(i => i.Name.In("en") == PosService.BarcodeProperty);
        context.AddRange(new ProductInfo { ElementId = beer.Id, TypeInfoId = barcode.Id, Value = JsonValue.Create("5901234123457"), TenantId = tenantId, OwnerId = company.Corporate },
            new ProductSalePrice { ProductId = beer.Id, Value = 150, CompanyId = company.Corporate, TenantId = tenantId, OwnerId = company.Corporate });
        await context.SaveChangesAsync(ct);
        (await pos.BarcodesAsync("en", ct)).Should().ContainSingle().Which.Should().Match<PosBarcodeDto>(b => b.Barcode == "5901234123457" && b.ProductId == beer.Id && b.Price == 150);
        var unit = (await context.Set<Unit>().FirstAsync(ct)).Id;
        var item = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ITEM", ct)).Id;
        var vat = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id;
        // The bar holds the beer it sells (H12): bought into the bar first.
        var purchase = (await context.Set<DocumentType>().ToListAsync(ct)).Single(t => t.Code == "FBM");
        var purchases = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == purchase.Id).Select(l => l.CategoryId).FirstAsync(ct);
        await documents.PostAsync((await documents.SaveAsync(new DocumentDto
        {
            TypeId = purchase.Id, CategoryIds = [purchases], PostingDate = new DateOnly(2026, 10, 31), FromId = supplier.Id, ToId = centres["BAR"].Id,
            Transactions = [new TransactionDto { TypeId = item, ElementId = beer.Id, ElementName = "Birrë", UnitId = unit, Quantity = 10, Price = 60, VatGroupId = vat }],
        }, ct)).Id!.Value, ct);
        // B5: of the bill's lines on products, the goods line holds the beer, the services line does not (only goods move stock).
        var serviceItem = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "SERVICE_ITEM", ct)).Id;
        var lineOf = await context.Set<TransactionTypeConf>().Where(c => c.TransactionTypeId == item || c.TransactionTypeId == serviceItem)
            .ToDictionaryAsync(c => c.Id, c => c.TransactionTypeId, ct);
        var proposal = await documents.LineProposalAsync(bar.BillTypeId!.Value, beer.Id, walkIn.Id, null, null, "en", ct);
        proposal.ScopeIds.Should().NotBeEmpty();
        proposal.ScopeIds.Select(id => lineOf.GetValueOrDefault(id)).Should().AllBeEquivalentTo((Guid?)item);

        // The founder (2026-10-31): a line whose element its kind does not hold refuses the whole document, and so does a document type that is not there.
        var servicesLine = lineOf.First(x => x.Value == serviceItem).Key;
        var before = await context.Set<Document>().CountAsync(ct);
        var goodsOnServices = async () => await documents.SaveAsync(new DocumentDto
        {
            TypeId = bar.BillTypeId!.Value, CategoryIds = [billCategory], PostingDate = new DateOnly(2026, 10, 31), ToId = walkIn.Id, ToName = "Klient",
            Transactions = [new TransactionDto { TypeId = item, ElementId = beer.Id, ElementName = "Birrë", UnitId = unit, Quantity = 1, Price = 150, VatGroupId = vat,
                FromId = centres["BAR"].Id }, new TransactionDto { TypeId = serviceItem, TypeConfId = servicesLine, ElementId = beer.Id, ElementName = "Birrë", UnitId = unit,
                Quantity = 1, Price = 150, VatGroupId = vat, FromId = centres["BAR"].Id }],
        }, ct);
        (await goodsOnServices.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("transactions[1].elementId");
        var noType = async () => await documents.SaveAsync(new DocumentDto { TypeId = Guid.NewGuid(), CategoryIds = [billCategory], PostingDate = new DateOnly(2026, 10, 31) }, ct);
        await noType.Should().ThrowAsync<ValidationException>();
        context.ChangeTracker.Clear();
        (await context.Set<Document>().CountAsync(ct)).Should().Be(before, "nothing of a refused document is kept");

        await documents.PostAsync((await documents.SaveAsync(new DocumentDto
        {
            TypeId = bar.BillTypeId!.Value, CategoryIds = [billCategory], DocumentDate = new DateOnly(2026, 10, 31), PostingDate = new DateOnly(2026, 10, 31), ToId = walkIn.Id,
            ToName = "Klient", TableId = centres["T1"].Id, PaymentMethodId = bar.PaymentMethodId,
            Transactions = [new TransactionDto { TypeId = item, ElementId = beer.Id, ElementTypeId = beer.TypeId, ElementName = "Birrë", UnitId = unit, Quantity = 2, Price = 150,
                VatGroupId = vat, FromId = centres["BAR"].Id, FromName = "Bar" }],
        }, ct)).Id!.Value, ct);

        var places = await pos.PlacesAsync(centres["BAR"].Id, "en", ct);
        places.Select(p => p.Code).Should().BeEquivalentTo("HALL", "T1", "T2");
        places.Single(p => p.Code == "HALL").Should().Match<PosPlaceDto>(p => p.HoldsPlaces && p.ParentId == null);
        var one = places.Single(p => p.Code == "T1");
        one.Should().Match<PosPlaceDto>(p => !p.HoldsPlaces && p.ParentId == centres["HALL"].Id && p.OpenBills == 1 && p.Open == 360);
        one.Waiters.Should().ContainSingle().Which.Name.Should().Be("Arben");
        one.Properties.Should().ContainSingle(p => p.Name == "Position X").Which.Value!.GetValue<int>().Should().Be(120);
        places.Single(p => p.Code == "T2").Open.Should().Be(0);

        // A waiter works at the bar itself: the bill belongs to the bar and posts with the company's scenarios.
        var atBar = new RequestContext(tenantId, user, centres["BAR"].Id, new HashSet<Guid>(), IsAdministrator: true);
        var barVisibility = new Visibility(context, atBar);
        var barDocuments = new DocumentService(context, atBar, barVisibility, TimeProvider.System);
        var written = await barDocuments.PostAsync((await barDocuments.SaveAsync(new DocumentDto
        {
            TypeId = bar.BillTypeId!.Value, CategoryIds = [billCategory], DocumentDate = new DateOnly(2026, 10, 31), PostingDate = new DateOnly(2026, 10, 31), ToId = walkIn.Id,
            ToName = "Klient", TableId = centres["T2"].Id, PaymentMethodId = bar.PaymentMethodId,
            Transactions = [new TransactionDto { TypeId = item, ElementId = beer.Id, ElementTypeId = beer.TypeId, ElementName = "Birrë", UnitId = unit, Quantity = 1, Price = 150,
                VatGroupId = vat, FromId = centres["BAR"].Id, FromName = "Bar" }],
        }, ct)).Id!.Value, ct);
        written.Posted.Should().BeTrue();
        (await new PosService(context, barVisibility, barDocuments, TimeProvider.System).PlacesAsync(centres["BAR"].Id, "en", ct)).Single(p => p.Code == "T2").Open.Should().Be(180);
    }
}

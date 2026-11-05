using System.Text.Json.Nodes;
using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Posting;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Posting;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>The document rules, on sales invoices (FSHM) of Fast Food Tirana and the Albanian pack.</summary>
public sealed class DocumentTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private OrganisationIds _org = null!;
    private Guid _admin;
    private Guid _seller;
    private Guid _invoice;
    private Guid _sales;
    private Guid _item;
    private Guid _vat20;
    private Guid _vat0;
    private Guid _eur;
    private Guid _unit;
    private Guid _client;
    private Guid _supplier;
    private Guid _cola;
    private Guid _oven;
    private Guid _burger;
    private Guid _gl;
    private Guid _scenario;
    private Guid _revenue;
    private Guid? _revenueType;
    private Guid _receivable;
    private Guid _cashAccount;
    private Guid _cashDesk;
    private Guid _warehouse;

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        await using var context = Context(null);
        await context.Database.MigrateAsync();
        CompanySetup[] companies = [new("ROMA", "Ristorante Roma", "IT"), new("TIRANA", "Fast Food Tirana", "AL", [("cost_center_type:WAREHOUSE", "WH1", "Warehouse"), ("cost_center_type:CASH_DESK", "CASH", "Cash desk")]), new("LIMASSOL", "Supermarket Limassol", "CY")];
        _org = Organisation.Create(context, Guid.CreateVersion7(), "Gamma Group Inc.", "US", companies);
        // Only Tirana takes its pack: the tests find its rows by their codes.
        Organisation.SetUpCompany(context, _org, companies.Single(c => c.Code == "TIRANA"));

        await context.SaveChangesAsync();
        var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = _org.TenantId };
        context.Add(staff);
        await context.SaveChangesAsync();
        var admin = new User { TypeId = staff.Id, Code = "ADMIN", Name = TranslatedText.English("Administrator"), TenantId = _org.TenantId };
        var seller = new User { TypeId = staff.Id, Code = "SELLER", Name = TranslatedText.English("Seller"), TenantId = _org.TenantId };
        context.AddRange(admin, seller);
        await context.SaveChangesAsync();
        (_admin, _seller) = (admin.Id, seller.Id);
        _invoice = (await context.Set<DocumentType>().SingleAsync(t => t.Code == "FSHM")).Id;
        _sales = (await context.Set<InterDocumentTypeCategory>().FirstAsync(l => l.TypeId == _invoice)).CategoryId;
        _item = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ITEM")).Id;
        var vat = await context.Set<VatGroup>().ToListAsync();
        (_vat20, _vat0) = (vat.Single(v => v.Rate == 20).Id, vat.Single(v => v.Rate == 0).Id);
        _eur = (await context.Set<Currency>().ToListAsync()).Single(c => c.Name?.In("en") == "EUR").Id;
        _unit = (await context.Set<Unit>().FirstAsync()).Id;
        // Real partners and a real product: balances and stock point at them.
        var tirana = _org.Companies["TIRANA"];
        var sme = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "SME")).Id;
        var client = new Partner { TypeId = sme, Code = "C1", Name = TranslatedText.English("Bar Lura"), TenantId = _org.TenantId, OwnerId = tirana };
        var supplier = new Partner { TypeId = sme, Code = "S1", Name = TranslatedText.English("Fresh Food"), TenantId = _org.TenantId, OwnerId = tirana };
        var burger = new Gamma.Erp.Models.Products.Product { TypeId = (await context.Set<Gamma.Erp.Models.Products.ProductType>().SingleAsync(t => t.Code == "GOODS")).Id,
            Code = "BURGER", Name = TranslatedText.English("Burger"), TenantId = _org.TenantId, OwnerId = tirana };
        var cola = new Gamma.Erp.Models.Products.Product { TypeId = burger.TypeId, Code = "COLA", Name = TranslatedText.English("Cola"), TenantId = _org.TenantId, OwnerId = tirana };
        // Goods are in the Products category, which the item line's scope holds (a line's element must be one of its kind's).
        var products = (await context.Set<Gamma.Erp.Models.Products.ProductCategory>().SingleAsync(c => c.Code == "PRODUCT" && c.OwnerId == tirana)).Id;
        var ovenAsset = new Gamma.Erp.Models.Products.Product { TypeId = (await context.Set<Gamma.Erp.Models.Products.ProductType>().SingleAsync(t => t.Code == "ASSET" && t.OwnerId == tirana)).Id,
            Code = "OVEN1", Name = TranslatedText.English("Oven"), TenantId = _org.TenantId, OwnerId = tirana };
        var assets = (await context.Set<Gamma.Erp.Models.Products.ProductCategory>().SingleAsync(c => c.Code == "ASSET" && c.OwnerId == tirana)).Id;
        context.AddRange(ovenAsset, new Gamma.Erp.Models.Products.InterProductCategory { ElementId = ovenAsset.Id, CategoryId = assets, TenantId = _org.TenantId, OwnerId = tirana });
        _oven = ovenAsset.Id;
        context.AddRange(client, supplier, burger, cola,
            new Gamma.Erp.Models.Products.InterProductCategory { ElementId = burger.Id, CategoryId = products, TenantId = _org.TenantId, OwnerId = tirana },
            new Gamma.Erp.Models.Products.InterProductCategory { ElementId = cola.Id, CategoryId = products, TenantId = _org.TenantId, OwnerId = tirana });
        await context.SaveChangesAsync();
        (_client, _supplier, _burger, _cola) = (client.Id, supplier.Id, burger.Id, cola.Id);
        // The Albanian pack posts sales through account lines (GLT): a sale's line takes an income account (class 7), whose scenario credits
        // it with the value and output VAT (4457) with the VAT, against the client's receivable (411). Sales of goods (705) here.
        _gl = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT")).Id;
        var charts = await context.Set<Chart>().ToListAsync();
        var revenue = charts.Single(c => c.Code == "705");
        (_revenue, _revenueType) = (revenue.Id, revenue.TypeId);
        var link = await context.Set<InterDocumentTypeTransactionType>().SingleAsync(l => l.DocumentTypeId == _invoice && l.TransactionTypeId == _gl);
        var incomeScope = context.Set<Elements>().Where(e => e.TypeId == revenue.TypeId).Select(e => (Guid?)e.Id);
        _scenario = (await context.Set<Scenario>().SingleAsync(s => s.DocumentTypeTransactionTypeId == link.Id && incomeScope.Contains(s.ElementsId))).Id;
        _receivable = charts.Single(c => c.Code == "411").Id;
        _cashAccount = charts.Single(c => c.Code == "5311").Id;
        _warehouse = (await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH1")).Id;
        _cashDesk = (await context.Set<CostCenter>().SingleAsync(c => c.Code == "CASH")).Id;
        // The cash desk holds its account (the "Account" property of the Cash desk type), as the guided start will ask.
        var cashType = (await context.Set<CostCenter>().SingleAsync(c => c.Id == _cashDesk)).TypeId;
        var chartLookups = context.Set<Elements>().Where(e => e.Table == "tbl_chart").Select(e => (Guid?)e.Id);
        var accountProperty = await context.Set<CostCenterTypeInfo>().SingleAsync(p => p.TypeId == cashType && p.Kind == PropertyKind.Lookup && chartLookups.Contains(p.ElementsId));
        context.Add(new CostCenterInfo { ElementId = _cashDesk, TypeInfoId = accountProperty.Id, Value = JsonValue.Create(_cashAccount.ToString()), TenantId = _org.TenantId, OwnerId = _org.Companies["TIRANA"] });
        await context.SaveChangesAsync();
    }

    /// <summary>A sale takes what the company holds (cost layers, H12): the warehouse buys its goods first.</summary>
    private async Task StockTheWarehouseAsync()
    {
        var request = new RequestContext(_org.TenantId, _admin, _org.Companies["TIRANA"], new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(request);
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var purchase = (await context.Set<DocumentType>().SingleAsync(t => t.Code == "FBM")).Id;
        var category = (await context.Set<InterDocumentTypeCategory>().FirstAsync(l => l.TypeId == purchase)).CategoryId;
        var asset = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "FIXED_ASSET")).Id;
        TransactionDto Line(Guid type, Guid product, string name) => new() { TypeId = type, ElementId = product, ElementName = name, UnitId = _unit, Quantity = 1000, Price = 1, VatGroupId = _vat0 };
        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = purchase, CategoryIds = [category], PostingDate = new DateOnly(2026, 1, 1), DocumentDate = new DateOnly(2026, 1, 1), FromId = _supplier, FromName = "Fresh Food", ToId = _warehouse,
            Transactions = [Line(_item, _burger, "Burger"), Line(_item, _cola, "Cola"), Line(asset, _oven, "Oven")],
        }, default);
        await documents.PostAsync(saved.Id!.Value, default);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private ErpDbContext Context(RequestContext? request)
    {
        var tenant = new TenantContext();
        if (request is not null)
        {
            tenant.Set(request.TenantId, request.UserId);
        }

        var options = PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(), _db.GetConnectionString())
            .AddInterceptors(new TenantConnectionInterceptor(tenant), new HistoryInterceptor(tenant, TimeProvider.System));
        return new ErpDbContext((DbContextOptions<ErpDbContext>)options.Options);
    }

    private RequestContext As(Guid user, string company = "TIRANA", bool administrator = true) =>
        new(_org.TenantId, user, _org.Companies[company], new HashSet<Guid>(), administrator);

    private static DocumentService Service(ErpDbContext db, RequestContext request) => new(db, request, new Visibility(db, request), TimeProvider.System);

    private TransactionDto Line(string item, decimal price, decimal quantity = 1, decimal? discount = null, Guid? vat = null) => new()
    {
        // An item line chooses its item and unit from the database, as its configuration (tbl_transaction_fill) says.
        TypeId = _item, ElementId = item switch { "Cola" => _cola, "Oven" => _oven, _ => _burger }, ElementName = item, UnitId = _unit, Price = price, Quantity = quantity, Discount = discount, VatGroupId = vat ?? _vat20,
    };

    private TransactionDto Sale(decimal price, decimal quantity = 1) => new()
    {
        TypeId = _gl, ElementId = _revenue, ElementTypeId = _revenueType, ElementName = "Sales", Price = price, Quantity = quantity, VatGroupId = _vat20,
    };

    private DocumentDto Invoice(params TransactionDto[] lines) => new()
    {
        TypeId = _invoice, CategoryIds = [_sales], DocumentDate = new DateOnly(2026, 10, 29), PostingDate = new DateOnly(2026, 10, 29),
        FromId = _warehouse, FromName = "Warehouse", ToId = _client, ToName = "Bar Lura", Transactions = lines,
    };

    [Fact]
    public async Task An_invoice_gets_its_types_next_code_and_its_lines_and_totals_by_the_old_formulas()
    {
        await StockTheWarehouseAsync();
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);

        var saved = await documents.SaveAsync(Invoice(Line("Burger", 500, 2, 50), Line("Cola", 200, 3)), ct);

        var type = await db.Set<DocumentType>().AsNoTracking().SingleAsync(t => t.Id == _invoice, ct);
        saved.Code.Should().Be($"FSHM{type.CodeCurrent - 1}", "the type's next code follows its code");
        saved.Name.Values.Should().Contain(type.Name.Values.Values.First(), "without a name a document carries its type's");
        saved.CompanyId.Should().Be(_org.Companies["TIRANA"]);
        saved.Posted.Should().BeFalse();
        saved.Transactions.Select(t => t.Code).Should().Equal("1", "2");
        var burger = saved.Transactions[0];
        // value = (500 − 50) × 2 = 900; VAT = 900 × 20 / 100 = 180; with VAT 1080; the base currency's rate is 1.
        (burger.Value, burger.Vat, burger.ValueVat, burger.ExchangeRate, burger.ValueLcu).Should().Be((900m, 180m, 1080m, 1m, 900m));
        saved.Transactions[1].Discount.Should().Be(0, "the discount is 0 by default");
        var total = saved.Totals.Should().ContainSingle().Subject;
        (total.Discount, total.Value, total.Vat, total.ValueVat).Should().Be((100m, 1500m, 300m, 1800m));

        var next = await documents.SaveAsync(Invoice(Line("Fries", 150)), ct);
        next.Code.Should().Be($"FSHM{type.CodeCurrent}");
    }

    [Fact]
    public async Task A_change_replaces_the_lines_and_a_foreign_currency_is_worked_out_in_the_local_one()
    {
        await StockTheWarehouseAsync();
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        var first = await documents.SaveAsync(Invoice(Line("Burger", 500)), ct);
        var noRate = () => documents.SaveAsync(first with { Transactions = [Line("Burger", 5) with { CurrencyId = _eur }] }, ct);
        (await noRate.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("transactions[0].exchangeRate");

        var changed = await documents.SaveAsync(first with { Transactions = [Line("Burger", 5, 2) with { CurrencyId = _eur, ExchangeRate = 100 }] }, ct);

        changed.Transactions.Should().ContainSingle().Which.Id!.Value.Should().NotBe(first.Transactions[0].Id!.Value, "transactions are never updated: switched off and written again");
        var line = changed.Transactions[0];
        (line.Value, line.ValueLcu, line.VatLcu, line.ValueVatLcu, line.CurrencyName).Should().Be((10m, 1000m, 200m, 1200m, "EUR"));
        changed.Totals.Should().ContainSingle().Which.ValueVatLcu.Should().Be(1200m);
        var all = await db.Set<Transaction>().IgnoreQueryFilters().Where(t => t.DocumentId == first.Id).ToListAsync(ct);
        all.Count(t => t.Active).Should().Be(1);
        all.Should().Contain(t => t.Id == first.Transactions[0].Id && !t.Active, "the old line stays, switched off");
    }

    [Fact]
    public async Task NotConfiguredError_and_NotAllowedError_follow_the_document_types_configuration()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        var vat0 = () => documents.SaveAsync(Invoice(Line("Water", 100, vat: _vat0)), ct);
        (await vat0.Should().ThrowAsync<ValidationException>()).Which.Errors["transactions[0].vatGroupId"].Should().StartWith(ErrorCodes.NotConfiguredError);
        var depreciation = await db.Set<TransactionType>().FirstAsync(t => t.Code == "DEPRECIATION", ct);
        var otherType = () => documents.SaveAsync(Invoice(Line("Water", 100) with { TypeId = depreciation.Id }), ct);
        (await otherType.Should().ThrowAsync<ValidationException>()).Which.Errors["transactions[0].typeId"].Should().StartWith(ErrorCodes.NotConfiguredError);
        (await db.Set<Document>().CountAsync(ct)).Should().Be(0, "a refused save leaves nothing behind");

        var seller = As(_seller, administrator: false);
        await using var sellerDb = Context(seller);
        var asSeller = Service(sellerDb, seller);
        (await asSeller.AllowedTypesAsync(null, ct)).Should().BeEmpty();
        var notGiven = () => asSeller.SaveAsync(Invoice(Line("Water", 100)), ct);
        (await notGiven.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.NotAllowedError);

        await documents.SaveTypeUsersAsync(_invoice, [_seller], [], ct);
        (await asSeller.AllowedTypesAsync(_sales, ct)).Should().ContainSingle(t => t.Id == _invoice);
    }

    [Fact]
    public async Task PostedError_a_posted_document_is_final()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        var invoice = await documents.SaveAsync(Invoice(Sale(500)), ct);

        var posted = await documents.PostAsync(invoice.Id!.Value, ct);

        posted.Posted.Should().BeTrue();
        posted.Transactions.Should().OnlyContain(t => t.Posted);
        var change = () => documents.SaveAsync(posted with { ToName = "Someone else" }, ct);
        (await change.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.PostedError);
        var delete = () => documents.DeleteAsync(invoice.Id!.Value, ct);
        (await delete.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.PostedError);
        var again = () => documents.PostAsync(invoice.Id!.Value, ct);
        await again.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task LockedError_a_document_held_by_one_user_is_read_only_for_the_others()
    {
        await StockTheWarehouseAsync();
        var ct = TestContext.Current.CancellationToken;
        var admin = As(_admin);
        var seller = As(_seller);
        await using var adminDb = Context(admin);
        await using var sellerDb = Context(seller);
        var mine = Service(adminDb, admin);
        var theirs = Service(sellerDb, seller);
        var first = await mine.SaveAsync(Invoice(Line("Burger", 500)), ct);
        var second = await mine.SaveAsync(Invoice(Line("Cola", 200)), ct);

        await mine.HoldAsync(first.Id!.Value, ct);

        (await theirs.HolderAsync(first.Id!.Value, ct)).Should().Be(_admin);
        (await theirs.DocumentAsync(first.Id!.Value, ct)).Should().NotBeNull("others may still read it");
        var change = () => theirs.SaveAsync(first with { ToName = "Someone else" }, ct);
        (await change.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.LockedError);
        var hold = () => theirs.HoldAsync(first.Id!.Value, ct);
        (await hold.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.LockedError);
        await mine.SaveAsync(first with { ToName = "Bar Lura 2" }, ct);

        await mine.HoldAsync(second.Id!.Value, ct);
        (await theirs.HolderAsync(first.Id!.Value, ct)).Should().BeNull("holding one document releases the user's others");
        await mine.ReleaseAllAsync(ct);
        (await theirs.HolderAsync(second.Id!.Value, ct)).Should().BeNull();
        await theirs.SaveAsync((await theirs.DocumentAsync(first.Id!.Value, ct))! with { ToName = "Bar Lura 3" }, ct);
    }

    [Fact]
    public async Task Created_from_another_document_and_switched_off_with_its_lines()
    {
        await StockTheWarehouseAsync();
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        var order = await documents.SaveAsync(Invoice(Line("Burger", 500)), ct);
        var invoice = await documents.SaveAsync(Invoice(Line("Burger", 500)) with { CreatedFromIds = [order.Id!.Value] }, ct);
        invoice.CreatedFromIds.Should().Equal(order.Id!.Value);

        await documents.DeleteAsync(invoice.Id!.Value, ct);

        (await documents.DocumentAsync(invoice.Id!.Value, ct)).Should().BeNull();
        (await db.Set<Transaction>().CountAsync(t => t.DocumentId == invoice.Id, ct)).Should().Be(0, "its transactions are switched off with it");
        (await db.Set<DocumentTotal>().CountAsync(t => t.DocumentId == invoice.Id, ct)).Should().Be(0);

        var roma = As(_admin, "ROMA");
        await using var romaDb = Context(roma);
        (await Service(romaDb, roma).DocumentAsync(order.Id!.Value, ct)).Should().BeNull("Ristorante Roma never sees Tirana's documents");
    }

    [Fact]
    public async Task Posting_writes_the_scenarios_entries_and_the_clients_control_account_and_they_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        (await new PostingService(db).CheckScenarioAsync(_scenario, ct)).Should().BeNull("the scenario credits the value and the VAT, against the client's debit");
        var invoice = await documents.SaveAsync(Invoice(Sale(1000)), ct);

        await documents.PostAsync(invoice.Id!.Value, ct);

        var entries = await db.Set<ChartValue>().Where(v => v.DocumentId == invoice.Id).ToListAsync(ct);
        entries.Should().HaveCount(3);
        (entries.Sum(e => e.Debit), entries.Sum(e => e.Credit)).Should().Be((1200m, 1200m), "value 1000 + VAT 200 on each side");
        entries.Should().ContainSingle(e => e.ChartId == _receivable && e.Debit == 1200m, "the scenario's line puts the client's side on the receivable");
        entries.Should().ContainSingle(e => e.ChartId == _revenue && e.Credit == 1000m);
        entries.Should().OnlyContain(e => e.CostCenterId == _warehouse && e.PostingDate == new DateOnly(2026, 10, 29) && e.CompanyId == _org.Companies["TIRANA"]);
        var signed = await db.Set<ChartCalcValue>().Where(v => v.DocumentId == invoice.Id).ToListAsync(ct);
        signed.Should().HaveCount(3);
        signed.Sum(v => v.Value).Should().Be(0, "+ debit and − credit");
    }

    [Fact]
    public async Task NoScenarioError_nothing_is_posted_when_a_transaction_has_no_scenario()
    {
        await StockTheWarehouseAsync();
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        var asset = (await db.Set<TransactionType>().SingleAsync(t => t.Code == "FIXED_ASSET", ct)).Id;
        var invoice = await documents.SaveAsync(Invoice(Sale(1000), Line("Oven", 500) with { TypeId = asset }), ct);

        var post = () => documents.PostAsync(invoice.Id!.Value, ct);

        (await post.Should().ThrowAsync<ValidationException>()).Which.Errors["transactions[1]"].Should().StartWith(ErrorCodes.NoScenarioError);
        (await db.Set<ChartValue>().CountAsync(v => v.DocumentId == invoice.Id, ct)).Should().Be(0, "all or nothing: the sale's entries are not written either");
        (await documents.DocumentAsync(invoice.Id!.Value, ct))!.Posted.Should().BeFalse();
    }

    [Fact]
    public async Task A_receipt_posts_the_money_to_the_account_the_cash_desk_holds()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var cashCategory = await db.Set<DocumentCategory>().SingleAsync(c => c.Code == "CASH_DESK", ct);
        var receiptTypes = db.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == cashCategory.Id).Select(l => l.TypeId);
        var receipt = await db.Set<DocumentType>().SingleAsync(t => t.Code == "MA" && receiptTypes.Contains(t.Id), ct);
        var link = await db.Set<InterDocumentTypeTransactionType>().SingleAsync(l => l.DocumentTypeId == receipt.Id && l.TransactionTypeId == _gl, ct);
        var account = await db.Set<Chart>().SingleAsync(c => c.Id == _receivable, ct); // the client's receivable is credited
        var documents = Service(db, request);
        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = receipt.Id, CategoryIds = [cashCategory.Id], PostingDate = new DateOnly(2026, 10, 29), FromId = _client, FromName = "Bar Lura",
            ToId = _cashDesk, ToName = "Cash desk",
            Transactions = [new TransactionDto { TypeId = _gl, ElementId = account.Id, ElementTypeId = account.TypeId, ElementName = "Bar Lura pays", Price = 300, VatGroupId = _vat0 }],
        }, ct);

        await documents.PostAsync(saved.Id!.Value, ct);

        var entries = await db.Set<ChartValue>().Where(v => v.DocumentId == saved.Id).ToListAsync(ct);
        entries.Should().HaveCount(2);
        entries.Should().ContainSingle(e => e.ChartId == _cashAccount && e.Debit == 300m, "the money goes to the account the cash desk holds");
        entries.Should().ContainSingle(e => e.ChartId == account.Id && e.Credit == 300m, "the line's own account (SELF) is credited");
        entries.Should().OnlyContain(e => e.CostCenterId == _cashDesk);
    }

    [Fact]
    public async Task UnbalancedError_a_scenario_that_cannot_balance_is_found_in_the_configuration()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Context(As(_admin));
        var posting = new PostingService(db);
        (await posting.UnbalancedScenariosAsync(_org.Companies["TIRANA"], ct)).Should().BeEmpty("every scenario of the pack balances");

        // A VAT line too many: the scenario no longer balances, and the check says so.
        var vat = await db.Set<ScenarioConf>().FirstAsync(l => l.ScenarioId == _scenario && l.Value == 3, ct);
        db.Add(new ScenarioConf { ScenarioId = _scenario, ChartId = vat.ChartId, Side = true, Value = 3, ValueElementsId = vat.ValueElementsId, Place = vat.Place,
            CostCenterPlace = vat.CostCenterPlace, TenantId = _org.TenantId, OwnerId = vat.OwnerId });
        await db.SaveChangesAsync(ct);

        (await posting.CheckScenarioAsync(_scenario, ct)).Should().StartWith(ErrorCodes.UnbalancedError);
        (await posting.UnbalancedScenariosAsync(_org.Companies["TIRANA"], ct)).Should().ContainKey(_scenario).And.HaveCount(1);
    }

    [Fact]
    public async Task GroupAccountError_nothing_is_posted_to_an_account_with_accounts_under_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        // The scenario's VAT line pointed at 445 (VAT), a group account of the chart: its accounts are 4453-4458.
        var group = await db.Set<Chart>().SingleAsync(c => c.Code == "445", ct);
        var vat = await db.Set<ScenarioConf>().SingleAsync(l => l.ScenarioId == _scenario && l.Value == 3, ct);
        vat.ChartId = group.Id;
        await db.SaveChangesAsync(ct);
        var documents = Service(db, request);
        var invoice = await documents.SaveAsync(Invoice(Sale(1000)), ct);

        var post = () => documents.PostAsync(invoice.Id!.Value, ct);

        (await post.Should().ThrowAsync<ValidationException>()).Which.Errors["entries.chart"].Should().StartWith(ErrorCodes.GroupAccountError).And.Contain("445");
        (await db.Set<ChartValue>().CountAsync(v => v.DocumentId == invoice.Id, ct)).Should().Be(0);
    }

    /// <summary>The group's chart at the corporate, with two accounts; Tirana maps the sale's accounts to them (its VAT account only when asked).</summary>
    private async Task<(Guid Receivables, Guid Income, string VatCode)> GroupChartAsync(ErpDbContext db, bool mapVat, CancellationToken ct)
    {
        var group = _org.Corporate;
        var chart = new ChartCategory { Code = "GROUP", Name = TranslatedText.English("Group chart"), IsElement = true, OwnerId = group };
        var type = new ChartType { Code = "GROUP", Name = TranslatedText.English("Group account"), IsElement = true, OwnerId = group };
        var receivables = new Chart { TypeId = type.Id, Code = "G1", Name = TranslatedText.English("Receivables"), OwnerId = group };
        var income = new Chart { TypeId = type.Id, Code = "G2", Name = TranslatedText.English("Income and taxes"), OwnerId = group };
        db.AddRange(chart, type, receivables, income, new InterChartCategory { ElementId = receivables.Id, CategoryId = chart.Id, OwnerId = group },
            new InterChartCategory { ElementId = income.Id, CategoryId = chart.Id, OwnerId = group });
        var tirana = _org.Companies["TIRANA"];
        var vat = (await db.Set<ScenarioConf>().SingleAsync(l => l.ScenarioId == _scenario && l.Value == 3, ct)).ChartId!.Value;
        db.AddRange(new InterChartChart { Chart1Id = _receivable, Chart2Id = receivables.Id, OwnerId = tirana }, new InterChartChart { Chart1Id = _revenue, Chart2Id = income.Id, OwnerId = tirana });
        if (mapVat)
        {
            db.Add(new InterChartChart { Chart1Id = vat, Chart2Id = income.Id, OwnerId = tirana });
        }

        await db.SaveChangesAsync(ct);
        return (receivables.Id, income.Id, (await db.Set<Chart>().SingleAsync(c => c.Id == vat, ct)).Code!);
    }

    [Fact]
    public async Task Posting_writes_the_same_entries_on_the_groups_chart_through_the_companys_mapping_with_its_country()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var (receivables, income, _) = await GroupChartAsync(db, mapVat: true, ct);
        var documents = Service(db, request);
        var invoice = await documents.SaveAsync(Invoice(Sale(1000)), ct);

        await documents.PostAsync(invoice.Id!.Value, ct);

        var entries = await db.Set<ChartValue>().Where(v => v.DocumentId == invoice.Id).ToListAsync(ct);
        entries.Should().HaveCount(6, "the three statutory entries and the same three on the group's chart");
        entries.Where(e => e.Country is null).Should().HaveCount(3, "the statutory entries carry no country");
        var group = entries.Where(e => e.Country == "AL").ToList();
        group.Should().HaveCount(3).And.OnlyContain(e => e.CompanyId == _org.Companies["TIRANA"] && e.CostCenterId == _warehouse);
        group.Should().ContainSingle(e => e.ChartId == receivables && e.Debit == 1200m);
        group.Where(e => e.ChartId == income).Sum(e => e.Credit).Should().Be(1200m, "the revenue 1000 and the VAT 200, both mapped to one account");
        (await db.Set<ChartCalcValue>().Where(v => v.DocumentId == invoice.Id && v.Country == "AL").SumAsync(v => v.Value, ct)).Should().Be(0);
    }

    [Fact]
    public async Task MappingError_nothing_is_posted_when_an_account_has_no_account_in_the_groups_chart()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var (_, _, vatCode) = await GroupChartAsync(db, mapVat: false, ct);
        var documents = Service(db, request);
        var invoice = await documents.SaveAsync(Invoice(Sale(1000)), ct);

        var post = () => documents.PostAsync(invoice.Id!.Value, ct);

        (await post.Should().ThrowAsync<ValidationException>()).Which.Errors["entries.GROUP"].Should().StartWith(ErrorCodes.MappingError).And.Contain(vatCode);
        (await db.Set<ChartValue>().CountAsync(v => v.DocumentId == invoice.Id, ct)).Should().Be(0, "neither chart gets anything");
    }

    [Fact]
    public async Task FillError_a_line_needs_what_its_configuration_says()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        // FSHM × item: the item and the unit are chosen from the database (1), the price is typed (0).
        var noUnit = () => documents.SaveAsync(Invoice(Line("Burger", 500) with { UnitId = null }), ct);
        (await noUnit.Should().ThrowAsync<ValidationException>()).Which.Errors["transactions[0].unit"].Should().StartWith(ErrorCodes.FillError);
        var typedItem = () => documents.SaveAsync(Invoice(Line("Burger", 500) with { ElementId = null }), ct);
        (await typedItem.Should().ThrowAsync<ValidationException>()).Which.Errors["transactions[0].element"].Should().Contain("chosen from the list");
        var noPrice = () => documents.SaveAsync(Invoice(Line("Burger", 500) with { Price = null }), ct);
        (await noPrice.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("transactions[0].price");
    }

    [Fact]
    public async Task A_line_without_quantity_or_discount_takes_its_configurations_defaults()
    {
        await StockTheWarehouseAsync();
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var link = await db.Set<InterDocumentTypeTransactionType>().SingleAsync(l => l.DocumentTypeId == _invoice && l.TransactionTypeId == _item, ct);
        var fill = await db.Set<TransactionFill>().SingleAsync(f => f.DocumentTypeTransactionTypeId == link.Id, ct);
        (fill.DefaultQuantity, fill.DefaultDiscount) = (2, 5);
        await db.SaveChangesAsync(ct);

        var saved = await Service(db, request).SaveAsync(Invoice(Line("Burger", 100) with { Quantity = null, Discount = null }), ct);

        var line = saved.Transactions.Single();
        (line.Quantity, line.Discount, line.Value).Should().Be((2m, 5m, 190m), "(100 − 5) × 2, from the row, not from code");
    }

    [Fact]
    public async Task A_line_typed_with_its_value_with_VAT_is_worked_out_back()
    {
        await StockTheWarehouseAsync();
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);

        var saved = await Service(db, request).SaveAsync(Invoice(Line("Menu", 0, quantity: 2, discount: 10) with { Price = null, ValueVat = 2400 }), ct);

        var line = saved.Transactions.Single();
        // VAT = 2400 / 120 × 20 = 400, value = 2000, unit price = 2000 / 2 + 10 = 1010.
        (line.ValueVat, line.Vat, line.Value, line.Price).Should().Be((2400m, 400m, 2000m, 1010m));
    }

    [Fact]
    public async Task A_hold_runs_out_after_the_companys_hold_timeout()
    {
        await StockTheWarehouseAsync();
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        var invoice = await documents.SaveAsync(Invoice(Line("Burger", 500)), ct);
        await documents.HoldAsync(invoice.Id!.Value, ct);
        (await documents.HolderAsync(invoice.Id!.Value, ct)).Should().Be(_admin, "with no timeout set, a hold lasts until it is released");

        var property = (await db.Set<CostCenterTypeInfo>().Where(p => p.Kind == PropertyKind.Number).ToListAsync(ct))
            .Single(p => p.Name.In("en") == DocumentService.HoldTimeoutProperty);
        db.Add(new CostCenterInfo { ElementId = _org.Companies["TIRANA"], TypeInfoId = property.Id, Value = JsonValue.Create(30m), OwnerId = _org.Companies["TIRANA"] });
        await db.SaveChangesAsync(ct);
        (await documents.HolderAsync(invoice.Id!.Value, ct)).Should().Be(_admin, "the hold is younger than 30 minutes");
        await db.Database.ExecuteSqlRawAsync("UPDATE documents.tbl_document_working SET created_on = now() - interval '31 minutes' WHERE document_id = {0}", [invoice.Id!.Value], ct);

        (await documents.HolderAsync(invoice.Id!.Value, ct)).Should().BeNull("the company's 30 minutes have run out");
    }

    [Fact]
    public async Task FillError_a_line_needs_its_warehouse_its_own_or_the_documents()
    {
        await StockTheWarehouseAsync();
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        var cash = _cashDesk;

        var nowhere = () => documents.SaveAsync(Invoice(Line("Burger", 500)) with { FromId = null, FromName = null }, ct);
        (await nowhere.Should().ThrowAsync<ValidationException>()).Which.Errors["transactions[0].location"].Should().StartWith(ErrorCodes.FillError);

        // Each line may come from its own warehouse; the document's is only the one a line without its own takes.
        var saved = await documents.SaveAsync(Invoice(Line("Burger", 500), Line("Cola", 200) with { FromId = cash, FromName = "Cash desk" }) with { FromId = null, FromName = null }
            with { Transactions = [Line("Burger", 500) with { FromId = _warehouse }, Line("Cola", 200) with { FromId = cash }] }, ct);
        saved.Transactions.Select(t => t.FromId).Should().Equal(_warehouse, cash);
    }

    private async Task<Guid> SettingAsync(ErpDbContext db, string table, decimal? allowed)
    {
        var tirana = _org.Companies["TIRANA"];
        var setting = (await (from p in db.Set<CostCenterTypeInfo>() join e in db.Set<Elements>() on p.ElementsId equals e.Id
                              where p.Kind == PropertyKind.Number && e.Table == table select p).ToListAsync())
            .Single(p => p.Name.In("en")!.StartsWith(MovementService.NegativeStockProperty, StringComparison.Ordinal));
        if (allowed is { } value)
        {
            db.Add(new CostCenterInfo { ElementId = tirana, TypeInfoId = setting.Id, Value = JsonValue.Create(value), OwnerId = tirana });
            await db.SaveChangesAsync();
        }

        return setting.Id;
    }

    private async Task<DocumentDto> GoodsAsync(DocumentService documents, ErpDbContext db, string typeCode, string categoryCode, Guid from, Guid to, decimal quantity, CancellationToken ct)
    {
        var category = await db.Set<DocumentCategory>().SingleAsync(c => c.Code == categoryCode, ct);
        var inCategory = db.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == category.Id).Select(l => l.TypeId);
        var type = await db.Set<DocumentType>().SingleAsync(t => t.Code == typeCode && inCategory.Contains(t.Id), ct);
        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = type.Id, CategoryIds = [category.Id], PostingDate = new DateOnly(2026, 10, 29), FromId = from, ToId = to,
            Transactions = [new TransactionDto { TypeId = _item, ElementId = _burger, ElementName = "Burger", UnitId = _unit, Quantity = quantity, Price = 2, VatGroupId = _vat20 }],
        }, ct);
        return await documents.PostAsync(saved.Id!.Value, ct);
    }

    [Fact]
    public async Task A_document_types_form_names_its_sides_lines_and_choices_and_a_line_proposes_the_price_by_side()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);

        // B4: the sale invoice's form, read from its configuration.
        var form = await documents.FormAsync(_invoice, "en", ct);
        form.Code.Should().Be("FSHM");
        form.To!.Table.Should().Be("tbl_partner", "a sale goes to a partner");
        form.LineKinds.Select(k => k.TransactionTypeId).Should().Contain([_item, _gl]);
        form.LineKinds.Single(k => k.TransactionTypeId == _item).Scopes.Should().Contain(s => s.Table == "tbl_product");
        form.VatGroups.Should().Contain(v => v.Id == _vat20 && v.Rate == 20);
        form.Currencies[0].IsBase.Should().BeTrue("the company's own currency comes first");
        var notGiven = () => Service(db, As(_seller, administrator: false)).FormAsync(_invoice, "en", ct);
        (await notGiven.Should().ThrowAsync<ValidationException>()).Which.Errors["typeId"].Should().Contain("NotAllowedError");

        // B5: a sale proposes the most particular sale price: the partner's type before the price for everyone.
        var tirana = _org.Companies["TIRANA"];
        var specific = new InterProductSpecificUnit { ProductId = _burger, UnitId = _unit, OwnerId = tirana };
        db.Add(specific);
        await db.SaveChangesAsync(ct);
        var clientType = (await db.Set<Partner>().SingleAsync(p => p.Id == _client, ct)).TypeId;
        db.AddRange(new ProductSalePrice { CompanyId = tirana, ProductId = _burger, ProductSpecificUnitId = specific.Id, Value = 3m, OwnerId = tirana },
            new ProductSalePrice { CompanyId = tirana, ProductId = _burger, ProductSpecificUnitId = specific.Id, PartnerTypeId = clientType, Value = 2.5m, OwnerId = tirana });
        await db.SaveChangesAsync(ct);
        var forClient = await documents.LineProposalAsync(_invoice, _burger, _client, null, null, "en", ct);
        (forClient.UnitId, forClient.Price, forClient.Source).Should().Be((_unit, 2.5m, DocumentService.SalePriceSource));
        forClient.Units.Should().ContainSingle().Which.Id.Should().Be(_unit);
        (await documents.LineProposalAsync(_invoice, _burger, null, null, null, "en", ct)).Price.Should().Be(3m, "no partner: the price for everyone");

        // A purchase proposes what the company last paid (the purchase invoice bought burgers at 2).
        await GoodsAsync(documents, db, "FBM", "PURCHASES", _supplier, _warehouse, 4, ct);
        var purchase = await db.Set<DocumentType>().SingleAsync(t => t.Code == "FBM", ct);
        var bought = await documents.LineProposalAsync(purchase.Id, _burger, _supplier, _unit, null, "en", ct);
        (bought.Price, bought.Source).Should().Be((2m, DocumentService.PurchasePriceSource));
    }

    [Fact]
    public async Task StockError_a_warehouse_does_not_go_negative_when_the_company_says_so_and_the_places_that_have_it_are_named()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        await SettingAsync(db, "tbl_cost_center", 0);
        await GoodsAsync(documents, db, "FBM", "PURCHASES", _supplier, _warehouse, 10, ct);
        await GoodsAsync(documents, db, "FBM", "PURCHASES", _supplier, _cashDesk, 20, ct);
        await GoodsAsync(documents, db, "FSHM", "SALES", _warehouse, _client, 4, ct);

        var oversell = () => GoodsAsync(documents, db, "FSHM", "SALES", _warehouse, _client, 7, ct);

        var refused = (await oversell.Should().ThrowAsync<ValidationException>()).Which.Errors["transactions[0].stock"];
        refused.Should().StartWith(ErrorCodes.StockError).And.Contain("there are 6").And.Contain("CASH (20)");
        var stock = await db.Set<InterCostCenterProductStock>().Where(s => s.CostCenterId == _warehouse).SingleAsync(ct);
        stock.Quantity.Should().Be(6, "10 bought − 4 sold, the refused sale changed nothing");
        (await db.Set<InterPartnerProductStock>().SingleAsync(s => s.PartnerId == _supplier, ct)).Quantity.Should().Be(-30,
            "the company's setting for partners is empty: their stock may go negative");
    }

    [Fact]
    public async Task Stock_may_go_negative_where_the_company_allows_it_and_balances_take_the_value_with_VAT()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        await SettingAsync(db, "tbl_cost_center", 1);
        // Nothing goes out that the company does not hold (H12): the burgers are at the cash desk, the warehouse may sell them short.
        await GoodsAsync(documents, db, "FBM", "PURCHASES", _supplier, _cashDesk, 5, ct);

        var sale = await GoodsAsync(documents, db, "FSHM", "SALES", _warehouse, _client, 5, ct);

        (await db.Set<InterCostCenterProductStock>().SingleAsync(s => s.CostCenterId == _warehouse, ct)).Quantity.Should().Be(-5);
        var balance = await db.Set<PartnerBalance>().SingleAsync(b => b.DocumentId == sale.Id, ct);
        (balance.PartnerId, balance.Debit, balance.Credit).Should().Be((_client, 12m, 0m), "5 × 2 = 10 + VAT 2, debit on the to side");
    }

    [Fact]
    public async Task Depreciation_runs_per_period_post_through_the_books_scenario_and_keep_the_remaining_value_per_book()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        var depreciation = new Gamma.Erp.Services.Depreciation.DepreciationService(db, request, documents);
        var tirana = _org.Companies["TIRANA"];
        var oven = new Product { TypeId = (await db.Set<ProductType>().SingleAsync(t => t.Code == "ASSET", ct)).Id, Code = "OVEN", Name = TranslatedText.English("Oven"),
            TenantId = _org.TenantId, OwnerId = tirana };
        db.Add(oven);
        await db.SaveChangesAsync(ct);
        db.Add(new InterProductCategory { ElementId = oven.Id, CategoryId = (await db.Set<ProductCategory>().SingleAsync(c => c.Code == "ASSET", ct)).Id, OwnerId = tirana });
        await db.SaveChangesAsync(ct);
        var books = await db.Set<Gamma.Erp.Models.Depreciation.DepreciationBook>().ToListAsync(ct);
        var (company, tax) = (books.Single(b => b.Posts).Id, books.Single(b => !b.Posts).Id);
        // B11 (C3 phase 7): the company's books, with their method and whether they post.
        var listed = await depreciation.BooksAsync("en", ct);
        listed.Select(b => (b.Id, b.Posts)).Should().BeEquivalentTo([(company, true), (tax, false)]);
        listed.Should().OnlyContain(b => b.Name.Length > 0 && b.Method != null);
        await depreciation.RegisterAsync(new(oven.Id, _unit, company, 12000, new DateOnly(2026, 1, 1), 60), ct);
        await depreciation.RegisterAsync(new(oven.Id, _unit, tax, 12000, new DateOnly(2026, 1, 1)), ct); // the life its category's scenario proposes
        var journalCategory = await db.Set<DocumentCategory>().SingleAsync(c => c.Code == "JOURNAL", ct);
        var inJournals = db.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == journalCategory.Id).Select(l => l.TypeId);
        var journalType = await db.Set<DocumentType>().SingleAsync(t => t.Code == "FAA" && inJournals.Contains(t.Id), ct);

        var january = (await depreciation.RunAsync(company, new DateOnly(2026, 1, 31), ct)).Single();
        var february = (await depreciation.RunAsync(company, new DateOnly(2026, 2, 28), ct)).Single();
        await depreciation.RunAsync(tax, new DateOnly(2026, 1, 31), ct);
        var journal = await depreciation.JournalAsync(company, new DateOnly(2026, 1, 31), journalType.Id, journalCategory.Id, ct);

        (january.Days, january.Amount, february.Days, february.Amount).Should().Be((30, 197.15m, 28, 184.01m), "12000 / 1826 days, per period, no 365-day stop");
        journal.Posted.Should().BeTrue("the company book posts");
        journal.IdSup.Should().NotBeNull("an FAA journal sits under its FA sheet");
        var entries = await db.Set<Gamma.Erp.Models.Charts.ChartValue>().Where(v => v.DocumentId == journal.Id).ToListAsync(ct);
        (entries.Sum(e => e.Debit), entries.Sum(e => e.Credit)).Should().Be((197.15m, 197.15m));
        (await depreciation.RemainingValueAsync(oven.Id, _unit, company, ct)).Should().Be(12000 - 197.15m - 184.01m);
        (await depreciation.RemainingValueAsync(oven.Id, _unit, tax, ct)).Should().Be(12000 - 197.15m, "each book keeps its own value: one run, straight-line over the proposed 60 months");
    }

    [Fact]
    public async Task A_books_assets_and_runs_are_listed_and_only_its_latest_run_is_undone_the_journal_reversed_with_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        var depreciation = new Gamma.Erp.Services.Depreciation.DepreciationService(db, request, documents);
        var tirana = _org.Companies["TIRANA"];
        var oven = new Product { TypeId = (await db.Set<ProductType>().SingleAsync(t => t.Code == "ASSET", ct)).Id, Code = "OVEN", Name = TranslatedText.English("Oven"),
            TenantId = _org.TenantId, OwnerId = tirana };
        db.Add(oven);
        await db.SaveChangesAsync(ct);
        db.Add(new InterProductCategory { ElementId = oven.Id, CategoryId = (await db.Set<ProductCategory>().SingleAsync(c => c.Code == "ASSET", ct)).Id, OwnerId = tirana });
        await db.SaveChangesAsync(ct);
        var books = await db.Set<Gamma.Erp.Models.Depreciation.DepreciationBook>().ToListAsync(ct);
        var (company, tax) = (books.Single(b => b.Posts).Id, books.Single(b => !b.Posts).Id);
        await depreciation.RegisterAsync(new(oven.Id, _unit, company, 12000, new DateOnly(2026, 1, 1), 60), ct);
        await depreciation.RegisterAsync(new(oven.Id, _unit, tax, 12000, new DateOnly(2026, 1, 1)), ct);
        var journalCategory = await db.Set<DocumentCategory>().SingleAsync(c => c.Code == "JOURNAL", ct);
        var inJournals = db.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == journalCategory.Id).Select(l => l.TypeId);
        var journalType = await db.Set<DocumentType>().SingleAsync(t => t.Code == "FAA" && inJournals.Contains(t.Id), ct);
        var (january, february) = (new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28));

        await depreciation.RunAsync(company, january, ct);
        await depreciation.RunAsync(company, february, ct);
        var first = await depreciation.JournalAsync(company, january, journalType.Id, journalCategory.Id, ct);
        var second = await depreciation.JournalAsync(company, february, journalType.Id, journalCategory.Id, ct);

        // The assets, with their book value, and the runs, the latest first.
        var asset = (await depreciation.AssetsAsync(company, "en", ct)).Should().ContainSingle().Which;
        (asset.Product, asset.InitialValue, asset.Depreciated, asset.BookValue, asset.LastRun).Should().Be(("Oven", 12000m, 381.16m, 11618.84m, february));
        asset.Method.Should().NotBeNullOrEmpty();
        (await depreciation.AssetHistoryAsync(asset.AssetValueId, ct)).Select(h => (h.To, h.Amount, h.Document)).Should().Equal((january, 197.15m, first.Code), (february, 184.01m, second.Code));
        var runs = await depreciation.RunsAsync(company, ct);
        runs.Select(r => (r.Date, r.Assets, r.Amount, r.Latest, r.Posted)).Should().Equal((february, 1, 184.01m, true, true), (january, 1, 197.15m, false, true));
        (await depreciation.RunLinesAsync(company, february, "en", ct)).Should().ContainSingle().Which.Product.Should().Be("Oven");

        // January stands under February: neither way of undoing it is open.
        var early = async () => await depreciation.UndoRunAsync(company, january, ct);
        await early.Should().ThrowAsync<ValidationException>();
        var fromTheDocuments = async () => await documents.ReverseAsync(first.IdSup!.Value, null, february, ct);
        (await fromTheDocuments.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain("later depreciation run");

        // The latest run, its journal posted: reversed, and the asset back at its value before it.
        await depreciation.UndoRunAsync(company, february, ct);
        (await depreciation.RunsAsync(company, ct)).Should().ContainSingle().Which.Date.Should().Be(january);
        (await depreciation.RemainingValueAsync(oven.Id, _unit, company, ct)).Should().Be(12000 - 197.15m);
        (await db.Set<Document>().AnyAsync(d => d.ReversedDocumentId == second.Id, ct)).Should().BeTrue("the journal was reversed the usual way");
        (await depreciation.RunAsync(company, february, ct)).Single().Amount.Should().Be(184.01m, "the period is open again");

        // A run with no journal just goes.
        await depreciation.UndoRunAsync(company, february, ct);
        (await depreciation.RunsAsync(company, ct)).Should().ContainSingle().Which.Date.Should().Be(january);

        // January's journal reversed from the documents, now the latest: its run is cancelled with it.
        await documents.ReverseAsync(first.IdSup!.Value, null, february, ct);
        (await depreciation.RunsAsync(company, ct)).Should().BeEmpty();
        (await depreciation.RemainingValueAsync(oven.Id, _unit, company, ct)).Should().Be(12000);

        // A book that does not post: its journal stays unposted and goes with the run.
        await depreciation.RunAsync(tax, january, ct);
        var draft = await depreciation.JournalAsync(tax, january, journalType.Id, journalCategory.Id, ct);
        draft.Posted.Should().BeFalse();
        await depreciation.UndoRunAsync(tax, january, ct);
        (await depreciation.RunsAsync(tax, ct)).Should().BeEmpty();
        (await db.Set<Document>().AnyAsync(d => d.Id == draft.Id, ct)).Should().BeFalse();
        var none = async () => await depreciation.UndoRunAsync(tax, january, ct);
        await none.Should().ThrowAsync<ValidationException>();

        // A method's formula is tried when it is saved.
        var config = new Gamma.Erp.Services.Configuration.ConfigurationService(db, request, new Visibility(db, request));
        var broken = async () => await config.SaveAsync("depreciation-methods", new System.Text.Json.Nodes.JsonObject { ["formula"] = "(VF/DT" }, ct);
        (await broken.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("formula");
        var unknown = async () => await config.SaveAsync("depreciation-methods", new System.Text.Json.Nodes.JsonObject { ["formula"] = "XX*2" }, ct);
        await unknown.Should().ThrowAsync<ValidationException>();
        (await config.SaveAsync("depreciation-methods", new System.Text.Json.Nodes.JsonObject { ["formula"] = "(VF/DT)*DK" }, ct))["formula"]!.GetValue<string>().Should().Be("(VF/DT)*DK");
    }

    [Fact]
    public async Task An_asset_takes_the_life_and_rate_its_books_scenario_proposes_for_its_category_and_land_is_not_depreciated()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var depreciation = new Gamma.Erp.Services.Depreciation.DepreciationService(db, request, Service(db, request));
        var tirana = _org.Companies["TIRANA"];
        var assetType = (await db.Set<ProductType>().SingleAsync(t => t.Code == "ASSET", ct)).Id;
        async Task<Guid> AssetAsync(string code, string category)
        {
            var asset = new Product { TypeId = assetType, Code = code, Name = TranslatedText.English(code), TenantId = _org.TenantId, OwnerId = tirana };
            db.Add(asset);
            await db.SaveChangesAsync(ct);
            db.Add(new InterProductCategory { ElementId = asset.Id, CategoryId = (await db.Set<ProductCategory>().SingleAsync(c => c.Code == category, ct)).Id, OwnerId = tirana });
            await db.SaveChangesAsync(ct);
            return asset.Id;
        }

        var tax = (await db.Set<Gamma.Erp.Models.Depreciation.DepreciationBook>().SingleAsync(b => !b.Posts, ct)).Id;
        var laptop = await AssetAsync("LAPTOP", "COMPUTERS");
        var shop = await AssetAsync("SHOP", "BUILDINGS");
        var plot = await AssetAsync("PLOT", "LAND");

        await depreciation.RegisterAsync(new(laptop, _unit, tax, 1200, new DateOnly(2026, 1, 1)), ct);
        await depreciation.RegisterAsync(new(shop, _unit, tax, 100000, new DateOnly(2026, 1, 1), LinearPercentage: 4), ct);
        var land = () => depreciation.RegisterAsync(new(plot, _unit, tax, 50000, new DateOnly(2026, 1, 1)), ct);

        var values = await (from v in db.Set<Gamma.Erp.Models.Depreciation.AssetValue>() join s in db.Set<InterProductSpecificUnit>() on v.ProductSpecificUnitId equals s.Id
                            select new { s.ProductId, v.Months }).ToListAsync(ct);
        var rates = await (from u in db.Set<Gamma.Erp.Models.Depreciation.AssetValueUnique>() join s in db.Set<InterProductSpecificUnit>() on u.ProductSpecificUnitId equals s.Id
                           select new { s.ProductId, u.LinearPercentage }).ToListAsync(ct);
        values.Single(v => v.ProductId == laptop).Months.Should().Be(48, "computers: 25 % a year in Albania");
        rates.Single(r => r.ProductId == laptop).LinearPercentage.Should().Be(25);
        values.Single(v => v.ProductId == shop).Months.Should().Be(240, "buildings: 5 % a year; what is typed (4 %) still wins over the proposal");
        rates.Single(r => r.ProductId == shop).LinearPercentage.Should().Be(4);
        await land.Should().ThrowAsync<ValidationException>("land has no life to depreciate over");
    }

    [Fact]
    public async Task A_partners_figures_ledger_and_ranking_come_from_its_posted_documents_by_category()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        await GoodsAsync(documents, db, "FBM", "PURCHASES", _supplier, _warehouse, 10, ct);
        await GoodsAsync(documents, db, "FSHM", "SALES", _warehouse, _client, 4, ct);
        var statistics = new Gamma.Erp.Services.Partners.PartnerStatisticsService(db, new Visibility(db, request));

        var figures = (await statistics.StatisticsAsync(_client, null, null, ct))!;
        var ledger = await statistics.LedgerAsync(_client, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), ct);
        var sales = await db.Set<DocumentCategory>().SingleAsync(c => c.Code == "SALES", ct);
        var ranking = await statistics.RankingAsync(sales.Id, null, null, 1, 10, ct);

        figures.Balance.Should().Be(9.6m, "4 × 2 = 8 + VAT 1.6, not paid");
        var category = figures.Categories.Should().ContainSingle().Subject;
        (category.CategoryId, category.Documents, category.Value, category.Vat, category.ValueVat).Should().Be((sales.Id, 1, 8m, 1.6m, 9.6m));
        figures.TopProducts.Should().ContainSingle().Which.Quantity.Should().Be(4);
        ledger.Select(r => r.Balance).Should().Equal(0m, 9.6m);
        ranking.Items.Should().ContainSingle().Which.Should().Match<Gamma.Erp.Models.Partners.Dto.PartnerRankDto>(r => r.PartnerId == _client && r.Debit == 9.6m);
        (await statistics.StatisticsAsync(_supplier, null, null, ct))!.Balance.Should().Be(-24m, "bought 10 × 2 = 20 + VAT 4, a credit to the supplier");
    }

    [Fact]
    public async Task ReversalError_a_posted_document_is_corrected_by_its_mirrored_reversal_never_beyond_what_is_left()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = As(_admin);
        await using var db = Context(request);
        var documents = Service(db, request);
        await GoodsAsync(documents, db, "FBM", "PURCHASES", _supplier, _warehouse, 10, ct);
        var sale = await GoodsAsync(documents, db, "FSHM", "SALES", _warehouse, _client, 6, ct);
        var line = sale.Transactions.Single();

        var reversal = await documents.ReverseAsync(sale.Id!.Value, new Dictionary<Guid, decimal> { [line.Id!.Value] = 2 }, new DateOnly(2026, 10, 30), ct);

        reversal.Posted.Should().BeTrue();
        reversal.CreatedFromIds.Should().Equal(sale.Id!.Value);
        (reversal.FromId, reversal.ToId).Should().Be((_client, _warehouse), "from and to are swapped");
        var copy = reversal.Transactions.Single();
        (copy.IdSup, copy.Quantity, copy.FromId, copy.ToId).Should().Be((line.Id, 2m, line.ToId, line.FromId));
        (await db.Set<InterCostCenterProductStock>().SingleAsync(s => s.CostCenterId == _warehouse, ct)).Quantity.Should().Be(6, "10 bought − 6 sold + 2 returned");
        (await documents.DocumentAsync(sale.Id!.Value, ct))!.Posted.Should().BeTrue("the original is never unposted");

        var tooMuch = () => documents.ReverseAsync(sale.Id!.Value, new Dictionary<Guid, decimal> { [line.Id!.Value] = 5 }, new DateOnly(2026, 10, 30), ct);
        (await tooMuch.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.ReversalError).And.Contain("4 left");
        var unposted = await documents.SaveAsync(Invoice(Line("Burger", 500)), ct);
        var notPosted = () => documents.ReverseAsync(unposted.Id!.Value, null, new DateOnly(2026, 10, 30), ct);
        (await notPosted.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.ReversalError);
        var noType = () => documents.ReverseAsync(reversal.Id!.Value, null, new DateOnly(2026, 10, 30), ct);
        (await noType.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.NotConfiguredError, "a reversal type has no reversal type of its own");
    }
}

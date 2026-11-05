using AwesomeAssertions;
using ClosedXML.Excel;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Employees;
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
using Gamma.Erp.Services.Printing;
using Gamma.Erp.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>The stock and sales reports: stock per place at the average cost, a product's movements, sales lines per place and partner, Excel.</summary>
public sealed class StockReportTests : IAsyncLifetime
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
    public async Task Stock_is_reported_per_place_a_product_is_tracked_through_its_documents_sales_are_grouped_and_all_of_it_goes_to_Excel()
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
        var supplier = new Partner { TypeId = ltd, Code = "S1", Name = TranslatedText.English("Birra Tirana"), TenantId = tenantId, OwnerId = company.Corporate };
        var goods = await context.Set<ProductCategory>().SingleAsync(c => c.Code == "PRODUCT", ct);
        var goodsType = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == goods.Id).Select(l => l.TypeId).FirstAsync(ct);
        var beer = new Product { TypeId = goodsType, Code = "BEER", Name = TranslatedText.English("Beer"), TenantId = tenantId, OwnerId = company.Corporate };
        context.AddRange(client, supplier, beer, new InterProductCategory { ElementId = beer.Id, CategoryId = goods.Id, TenantId = tenantId, OwnerId = company.Corporate });
        await context.SaveChangesAsync(ct);
        var (first, second) = (await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH1", ct), await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH2", ct));
        var unit = (await context.Set<Unit>().FirstAsync(ct)).Id;
        var item = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ITEM", ct)).Id;
        var vat20 = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id;
        var vat0 = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 0, ct)).Id;
        var types = await context.Set<DocumentType>().ToListAsync(ct);
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);

        async Task PostAsync(string code, (Guid Id, string Name) from, (Guid Id, string Name) to, decimal quantity, decimal price, Guid vat, int day)
        {
            var type = types.Single(t => t.Code == code && t.IdSup == null);
            var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == type.Id).Select(l => l.CategoryId).FirstAsync(ct);
            var date = new DateOnly(2026, 10, day);
            var saved = await documents.SaveAsync(new DocumentDto
            {
                TypeId = type.Id, CategoryIds = [category], DocumentDate = date, PostingDate = date, FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name,
                Transactions = [new TransactionDto { TypeId = item, ElementId = beer.Id, ElementTypeId = beer.TypeId, ElementName = "Beer", UnitId = unit, Quantity = quantity,
                    Price = price, VatGroupId = vat }],
            }, ct);
            await documents.PostAsync(saved.Id!.Value, ct);
        }

        await PostAsync("FBM", (supplier.Id, "Birra Tirana"), (first.Id, "Magazina 1"), 10, 50, vat20, 1);
        await PostAsync("FTM", (first.Id, "Magazina 1"), (second.Id, "Magazina 2"), 4, 50, vat0, 2);
        await PostAsync("FSHM", (first.Id, "Magazina 1"), (client.Id, "Bar Lura"), 3, 100, vat20, 3);

        var visibility = new Visibility(context, request);
        var reports = new StockReportService(context, visibility);
        var stock = await reports.StockAsync(null, null, "en", ct);
        stock.Select(s => (s.CostCenter, s.Quantity)).Should().BeEquivalentTo([("Magazina 1", 3m), ("Magazina 2", 4m)], "10 bought, 4 moved, 3 sold");
        stock.Select(s => (s.CostCenter, s.Value)).Should().BeEquivalentTo([("Magazina 1", 150m), ("Magazina 2", 200m)],
            "each place's value from its cost layers: bought at 50 without VAT, 4 carried to Magazina 2");
        (await reports.StockAsync(second.Id, goods.Id, "en", ct)).Should().ContainSingle().Which.Quantity.Should().Be(4);

        var tracked = await reports.ItemTrackingAsync(beer.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "en", ct);
        tracked.Select(t => (t.From, t.To, t.Quantity)).Should().Equal(("Birra Tirana", "Magazina 1", 10m), ("Magazina 1", "Magazina 2", 4m), ("Magazina 1", "Bar Lura", 3m));

        var sales = await context.Set<DocumentCategory>().SingleAsync(c => c.Code == "SALES", ct);
        var sold = await reports.LinesAsync([sales.Id], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null, null, "en", ct);
        (sold.Value, sold.Total).Should().Be((300m, 360m));
        sold.ByCostCenter.Should().ContainSingle().Which.Name.Should().Be("Magazina 1");
        sold.ByPartner.Should().ContainSingle().Which.Name.Should().Be("Bar Lura");
        (await reports.LinesAsync([sales.Id], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), second.Id, null, "en", ct)).Lines.Should().BeEmpty();
        (sold.ByPartner.Single().Id, sold.ByCostCenter.Single().Id).Should().Be((client.Id, first.Id));

        // A transfer: from where to where, at what it cost.
        var transfers = await context.Set<DocumentCategory>().SingleAsync(c => c.Code == "TRANSFERS", ct);
        var moved = (await reports.LinesAsync([transfers.Id], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null, null, "en", ct)).Lines.Should().ContainSingle().Which;
        (moved.From, moved.To, moved.Quantity, moved.Cost).Should().Be(("Magazina 1", "Magazina 2", 4m, 200m));

        // Goods with a member of staff: the stock report shows them on asking, their ledger shows the debt.
        var staffType = (await context.Set<EmployeeType>().FirstAsync(t => t.IsElement, ct)).Id;
        var arben = new Employee { TypeId = staffType, Code = "E1", Name = TranslatedText.English("Arben"), TenantId = tenantId, OwnerId = company.Corporate };
        context.Add(arben);
        await context.SaveChangesAsync(ct);
        await PostAsync(types.Single(t => t.Name.In("en") == "Goods transfer, cost centre to staff").Code!, (first.Id, "Magazina 1"), (arben.Id, "Arben"), 2, 50, vat0, 4);
        var held = (await reports.StockAsync(null, null, StockReportService.StaffHolder, "en", ct)).Should().ContainSingle().Which;
        (held.CostCenter, held.Quantity, held.Holder).Should().Be(("Arben", 2m, StockReportService.StaffHolder));
        (await reports.StockAsync(null, null, StockReportService.PartnerHolder, "en", ct)).Select(s => (s.CostCenter, s.Quantity)).Should()
            .BeEquivalentTo([("Bar Lura", 3m), ("Birra Tirana", -10m)], "a running position");
        (await reports.StockAsync(null, null, "en", ct)).Should().OnlyContain(s => s.Holder == StockReportService.PlaceHolder, "the places unless asked");
        (await reports.StockAsync(null, null, StockReportService.AllHolders, "en", ct)).Should().HaveCount(5);
        (await reports.StockAsync(first.Id, null, StockReportService.AllHolders, "en", ct)).Should().ContainSingle("a cost centre filter names a place");
        var unknown = async () => await reports.StockAsync(null, null, "elsewhere", "en", ct);
        await unknown.Should().ThrowAsync<ValidationException>();

        var ledgers = new ReportService(context, visibility);
        var ledger = (await ledgers.EmployeeLedgerAsync(arben.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), ct))!;
        ledger[0].Balance.Should().Be(0, "nothing before the period");
        ledger.Should().HaveCount(2);
        ledger[1].Balance.Should().Be(ledger[1].Debit - ledger[1].Credit).And.NotBe(0);
        (await ledgers.EmployeeLedgerAsync(arben.Id, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 31), ct))!.Should().ContainSingle().Which.Balance.Should().Be(ledger[1].Balance, "the balance carried in");
        (await ledgers.EmployeeLedgerAsync(Guid.CreateVersion7(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), ct)).Should().BeNull();

        // Totals per document type, posted only.
        var total = await ledgers.DocumentTotalsAsync([sales.Id], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "en", ct);
        var invoices = total.Rows.Should().ContainSingle().Which;
        (invoices.Documents, invoices.Value, invoices.Vat, invoices.Total).Should().Be((1, 300m, 60m, 360m));
        (total.Documents, total.Total).Should().Be((1, 360m));

        // What the beer was bought at: the unit price of each purchase; a reversed one stays, with its reversal.
        var october = (new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var price = (await reports.PurchasePricesAsync(beer.Id, october.Item1, october.Item2, ct)).Should().ContainSingle().Which;
        (price.Supplier, price.Quantity, price.UnitPrice, price.Value, price.ReversedBy).Should().Be(("Birra Tirana", 10m, 50m, 500m, null));
        await PostAsync("FBM", (supplier.Id, "Birra Tirana"), (first.Id, "Magazina 1"), 5, 60, vat20, 6);
        var prices = await reports.PurchasePricesAsync(beer.Id, october.Item1, october.Item2, ct);
        prices.Select(p => p.UnitPrice).Should().Equal(50m, 60m);
        (await reports.PurchasePricesAsync(beer.Id, new DateOnly(2026, 10, 2), october.Item2, ct)).Should().ContainSingle().Which.UnitPrice.Should().Be(60, "the period counts");
        context.ChangeTracker.Clear();
        await documents.ReverseAsync(prices[1].DocumentId, null, new DateOnly(2026, 10, 7), ct);
        (await reports.PurchasePricesAsync(beer.Id, october.Item1, october.Item2, ct)).Select(p => p.ReversedBy is null).Should().Equal(true, false);

        using var book = new XLWorkbook(new MemoryStream(ExcelExport.Write(stock, "stock")));
        var sheet = book.Worksheet("stock");
        sheet.Cell(1, 2).GetString().Should().Be("Cost Center");
        sheet.Column(8).CellsUsed().Skip(1).Select(c => c.GetValue<decimal>()).Should().BeEquivalentTo(stock.Select(s => s.Quantity), "the quantities as numbers");
    }
}

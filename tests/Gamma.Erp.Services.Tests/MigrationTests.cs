using AwesomeAssertions;
using ClosedXML.Excel;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Employees;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Currencies;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Migration;
using Gamma.Erp.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>The Migration menu: a check saves nothing, an import saves every row or none, a step waits for what it needs, and an export comes back in.</summary>
public sealed class MigrationTests : IAsyncLifetime
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

    private static MemoryStream File(params string[][] rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add("Data");
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c].Length > 0)
                {
                    sheet.Cell(r + 1, c + 1).SetValue(rows[r][c]);
                }
            }
        }

        var output = new MemoryStream();
        book.SaveAs(output);
        output.Position = 0;
        return output;
    }

    private async Task<(MigrationService Service, ErpDbContext Context, Guid Tenant, Guid Owner, RequestContext Request)> SetUpAsync(CancellationToken ct)
    {
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", [("cost_center_type:WAREHOUSE", "WH1", "Magazina 1")]);
        OrganisationIds company;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            await db.SaveChangesAsync(ct);
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, null);
        var context = Context(tenant);
        var request = new RequestContext(tenantId, null, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        return (Service(context, request), context, tenantId, company.Corporate, request);
    }

    private static MigrationService Service(ErpDbContext context, RequestContext request)
    {
        var visibility = new Visibility(context, request);
        var entities = new MigrationEntities(new Dictionary<string, IEntityRows>(StringComparer.Ordinal)
        {
            ["documents"] = new DocumentService(context, request, visibility, TimeProvider.System).Entities,
            ["transactions"] = new EntityService<Gamma.Erp.Models.Transactions.TransactionCategory, Gamma.Erp.Models.Transactions.TransactionType, Gamma.Erp.Models.Transactions.InterTransactionTypeCategory, Gamma.Erp.Models.Transactions.TransactionTypeInfo, Gamma.Erp.Models.Transactions.TransactionTypeInfoChoice, Gamma.Erp.Models.Transactions.Transaction, Gamma.Erp.Models.Transactions.TransactionInfo, Gamma.Erp.Models.Transactions.InterTransactionCategory, Gamma.Erp.Models.Transactions.TransactionAccess>(context, request, visibility),
            ["charts"] = new EntityService<Gamma.Erp.Models.Charts.ChartCategory, Gamma.Erp.Models.Charts.ChartType, Gamma.Erp.Models.Charts.InterChartTypeCategory, Gamma.Erp.Models.Charts.ChartTypeInfo, Gamma.Erp.Models.Charts.ChartTypeInfoChoice, Gamma.Erp.Models.Charts.Chart, Gamma.Erp.Models.Charts.ChartInfo, Gamma.Erp.Models.Charts.InterChartCategory, Gamma.Erp.Models.Charts.ChartAccess>(context, request, visibility),
            ["partners"] = new EntityService<PartnerCategory, PartnerType, InterPartnerTypeCategory, PartnerTypeInfo, PartnerTypeInfoChoice, Partner, PartnerInfo, InterPartnerCategory, PartnerAccess>(context, request, visibility),
            ["products"] = new EntityService<ProductCategory, ProductType, InterProductTypeCategory, ProductTypeInfo, ProductTypeInfoChoice, Product, ProductInfo, InterProductCategory, ProductAccess>(context, request, visibility),
            ["cost-centers"] = new EntityService<CostCenterCategory, CostCenterType, InterCostCenterTypeCategory, CostCenterTypeInfo, CostCenterTypeInfoChoice, CostCenter, CostCenterInfo, InterCostCenterCategory, CostCenterAccess>(context, request, visibility)
                { Check = CostCenterRules.CompanyLevel(context) },
            ["units"] = new EntityService<UnitCategory, UnitType, InterUnitTypeCategory, UnitTypeInfo, UnitTypeInfoChoice, Unit, UnitInfo, InterUnitCategory, UnitAccess>(context, request, visibility),
            ["employees"] = new EntityService<EmployeeCategory, EmployeeType, InterEmployeeTypeCategory, EmployeeTypeInfo, EmployeeTypeInfoChoice, Employee, EmployeeInfo, InterEmployeeCategory, EmployeeAccess>(context, request, visibility),
        });
        var currencies = new CurrencyService(context, request, visibility);
        var documents = new DocumentService(context, request, visibility, TimeProvider.System);
        return new MigrationService(context, request, visibility, entities, currencies, new ProductUnitService(context, request, visibility), documents, new Gamma.Erp.Services.Depreciation.DepreciationService(context, request, documents));
    }

    [Fact]
    public async Task A_check_saves_nothing_an_import_saves_every_row_or_none_and_an_export_comes_back_in()
    {
        var ct = TestContext.Current.CancellationToken;
        var (migration, context, _, owner, _) = await SetUpAsync(ct);
        await using var scope = context;

        var steps = await migration.StepsAsync("en", ct);
        var codes = steps.Select(s => s.Code).ToList();
        foreach (var (before, after) in new[] { ("DOCUMENT_CATEGORIES", "DOCUMENT_TYPES"), ("DOCUMENT_TYPES", "LINE_CATEGORIES"), ("LINE_CATEGORIES", "LINE_TYPES"), ("LINE_TYPES", "LINE_KINDS"), ("LINE_KINDS", "POSTING_GROUPS"), ("CHART", "POSTING_GROUPS"),
            ("POSTING_GROUPS", "OPENING_PARTNERS"), ("OPENING_PARTNERS", "OPENING_CHART"), ("OPENING_CASH", "OPENING_CHART"), ("OPENING_STOCK", "OPENING_CHART"), ("OPENING_ASSETS", "OPENING_CHART") })
        {
            codes.IndexOf(before).Should().BeLessThan(codes.IndexOf(after), $"{after} needs {before}");
        }

        steps.Single(s => s.Code == "POSTING_GROUPS").Needs.Should().HaveCount(2);
        var names = steps.Select(s => s.Name).ToList();
        steps.Should().AllSatisfy(s => s.Needs!.Should().OnlyContain(n => names.IndexOf(n) < names.IndexOf(s.Name), $"{s.Code} shows after what it needs"));
        steps.Should().OnlyContain(s => s.Parent == null ? s.Needs!.Count == 0 : codes.Contains(s.Parent));
        steps.Single(s => s.Code == "PRICES").Waits.Should().ContainSingle().Which.Should().Be("Products and services", "no product is there yet");
        var early = () => migration.ImportAsync("PRICES", File(["Product", "Price"], ["P1", "10"]), "p.xlsx", false, ct);
        (await early.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("step");

        using (var template = new XLWorkbook(new MemoryStream(await migration.TemplateAsync("PARTNERS", ct))))
        {
            var headings = template.Worksheet("Data").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            headings.Should().StartWith(["Code", "Name", "Name (sq)", "Type", "Category"], "names come in English and the company's language").And.Contain("Tax number", "the columns come from the configured properties");
            template.Worksheet("Allowed values").Search("CLIENTS").Should().NotBeEmpty();
        }

        var good = new[] { "Code", "Name", "Name (sq)", "Type", "Category", "Tax number" };
        (await migration.ImportAsync("PARTNERS", File(good, ["C1", "Bar Lura", "Bar Lura", "LTD", "CLIENTS", "K12345678A"]), "c.xlsx", false, ct))
            .Should().BeEquivalentTo(new { Rows = 1, Added = 1, Updated = 0, Saved = false, Errors = Array.Empty<MigrationErrorDto>() });
        (await context.Set<Partner>().CountAsync(p => p.OwnerId == owner && p.Code == "C1", ct)).Should().Be(0, "a check saves nothing");

        var wrong = await migration.ImportAsync("PARTNERS", File(good, ["C1", "Bar Lura", "Bar Lura", "LTD", "CLIENTS", "K1"], ["C2", "Bar Vala", "Bar Vala", "NOPE", "CLIENTS", ""], ["C1", "Twice", "Dy here", "LTD", "CLIENTS", ""]), "c.xlsx", true, ct);
        wrong.Saved.Should().BeFalse();
        wrong.Errors.Select(e => (e.Row, e.Column)).Should().BeEquivalentTo([(3, "Type"), (4, "Code")]);
        (await context.Set<Partner>().CountAsync(p => p.Code == "C1", ct)).Should().Be(0, "one wrong row keeps every row out");

        var saved = await migration.ImportAsync("PARTNERS", File(good, ["C1", "=1+1", "Bar i Formulës", "LTD", "CLIENTS", "K12345678A"], ["", "Bar Vala", "Bar Vala", "SME", "CLIENTS", ""]), "c.xlsx", true, ct);
        saved.Should().BeEquivalentTo(new { Added = 2, Updated = 0, Saved = true });
        var step = (await migration.StepsAsync("en", ct)).Single(s => s.Code == "PARTNERS");
        step.Rows.Should().Be(2);
        step.Last!.Should().BeEquivalentTo(new { FileName = "c.xlsx", Added = 2, Updated = 0 });

        var bytes = await migration.ExportAsync("PARTNERS", ct);
        using (var export = new XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = export.Worksheet("Data");
            var row = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == "C1");
            row.Cell(2).HasFormula.Should().BeFalse("a name that looks like a formula stays a text");
            row.Cell(2).GetString().Should().Be("=1+1");
            sheet.Cell(1, sheet.Row(1).CellsUsed().First(c => c.GetString() == "Tax number").Address.ColumnNumber).GetString().Should().Be("Tax number");
            row.CellsUsed().Select(c => c.GetString()).Should().Contain("K12345678A");
        }

        var back = await migration.ImportAsync("PARTNERS", new MemoryStream(bytes), "back.xlsx", true, ct);
        back.Errors.Should().BeEmpty();
        back.Should().BeEquivalentTo(new { Added = 0, Updated = 2, Saved = true }, "an export comes back in");
        (await context.Set<Partner>().AsNoTracking().SingleAsync(p => p.Code == "C1", ct)).Name.In("sq").Should().Be("Bar i Formulës");
        (await migration.ImportAsync("PARTNERS", File(["Code", "Name", "Type", "Category"], ["C7", "Pa shqip", "LTD", "CLIENTS"]), "x.xlsx", true, ct)).Errors.Should().ContainSingle(e => e.Column == "Name (sq)", "a new row needs both names");
        (await migration.ImportAsync("PARTNERS", File(["Code", "Name", "Wrong"], ["C1", "x", "y"]), "x.xlsx", true, ct)).Errors.Should().ContainSingle(e => e.Column == "Wrong");
        (await migration.ImportAsync("PARTNERS", new MemoryStream([1, 2, 3]), "x.xlsx", true, ct)).Errors.Should().ContainSingle(e => e.Message.Contains("xlsx"));

        using var formula = new XLWorkbook();
        var cells = formula.Worksheets.Add("Data");
        (cells.Cell(1, 1).Value, cells.Cell(1, 2).Value, cells.Cell(1, 3).Value, cells.Cell(1, 4).Value, cells.Cell(1, 5).Value) = ("Code", "Name", "Type", "Category", "Name (sq)");
        (cells.Cell(2, 1).Value, cells.Cell(2, 3).Value, cells.Cell(2, 4).Value) = ("C9", "LTD", "CLIENTS");
        cells.Cell(2, 2).FormulaA1 = "1+1";
        var stream = new MemoryStream();
        formula.SaveAs(stream);
        stream.Position = 0;
        (await migration.ImportAsync("PARTNERS", stream, "f.xlsx", true, ct)).Errors.Should().ContainSingle(e => e.Message.Contains("formula"));
    }

    [Fact]
    public async Task Every_list_of_the_first_stage_goes_in_in_order()
    {
        var ct = TestContext.Current.CancellationToken;
        var (migration, context, _, owner, _) = await SetUpAsync(ct);
        await using var scope = context;
        var run = async (string step, MemoryStream file) =>
        {
            var result = await migration.ImportAsync(step, file, "f.xlsx", true, ct);
            result.Errors.Should().BeEmpty(step);
            result.Saved.Should().BeTrue(step);
            return result;
        };

        var money = await context.Set<Currency>().AsNoTracking().FirstAsync(c => c.OwnerId == owner && c.IsBase, ct);
        var account = await context.Set<Gamma.Erp.Models.Charts.Chart>().AsNoTracking().Where(c => c.OwnerId == owner && c.Code != null && c.Code.StartsWith("512")).OrderByDescending(c => c.Code).FirstAsync(ct);
        await run("COST_CENTERS", File(["Code", "Name", "Name (sq)", "Type", "Category", "Currency", "Account"], ["BK1", "National Bank", "Banka Kombëtare", "BANK", Code(context.Set<CostCenterCategory>().AsNoTracking().First(c => c.IsElement)), money.Code!, account.Code!]));
        var bank = await context.Set<CostCenter>().SingleAsync(c => c.Code == "BK1", ct);
        bank.IdSup.Should().Be(owner, "a cost centre with no parent sits under the company");
        using (var export = new XLWorkbook(new MemoryStream(await migration.ExportAsync("COST_CENTERS", ct))))
        {
            export.Worksheet("Data").RowsUsed().Single(r => r.Cell(1).GetString() == "BK1").CellsUsed().Select(c => c.GetString()).Should().Contain(money.Code!, "a lookup exports its code, never an id");
        }


        await run("PARTNER_CATEGORIES", File(["Code", "Name", "Name (sq)", "Parent"], ["VIP", "VIP clients", "Klientë VIP", "CLIENTS"]));
        (await context.Set<PartnerCategory>().SingleAsync(c => c.Code == "VIP", ct)).IdSup.Should().Be((await context.Set<PartnerCategory>().SingleAsync(c => c.Code == "CLIENTS", ct)).Id);
        await run("PRODUCT_CATEGORIES", File(["Code", "Name", "Name (sq)"], ["PIJE", "Drinks", "Pije"]));
        await run("UNITS", File(["Code", "Name", "Name (sq)", "Type", "Category"], ["KRT", "Cartons", "Kartonë", TypeOf(context), UnitCategoryOf(context)]));

        var products = await run("PRODUCTS", File(["Code", "Name", "Name (sq)", "Type", "Category", "Units"], ["P1", "Water", "Ujë", "GOODS", "PIJE", "KRT"]));
        products.Added.Should().Be(1);
        await run("PARTNERS", File(["Code", "Name", "Name (sq)", "Type", "Category"], ["C1", "Bar Lura", "Bar Lura", "LTD", "VIP"]));

        await run("PRICES", File(["Product", "Unit", "Client category", "Price"], ["P1", "KRT", "VIP", "95.5"], ["P1", "", "", "100"]));
        var prices = await context.Set<ProductSalePrice>().Where(p => p.CompanyId == owner).ToListAsync(ct);
        prices.Should().HaveCount(2);
        prices.Should().ContainSingle(p => p.PartnerCategoryId != null && p.ProductSpecificUnitId != null).Which.ValueLcu.Should().Be(95.5m);
        (await run("PRICES", File(["Product", "Price"], ["P1", "110"]))).Updated.Should().Be(1);

        await run("STAFF", File(["Code", "Name", "Name (sq)", "Type", "Category"], ["E1", "Ana Hoxha", "Ana Hoxha", "WORKER", Code(context.Set<EmployeeCategory>().AsNoTracking().First(c => c.IsElement))]));

        var euro = await context.Set<Currency>().AsNoTracking().FirstAsync(c => c.OwnerId == owner && !c.IsBase, ct);
        (await run("RATES", File(["Currency", "Date", "Rate"], [euro.Code!, "2026-10-31", "100.5"]))).Added.Should().Be(1);
        (await run("RATES", File(["Currency", "Date", "Rate"], [euro.Code!, "2026-10-31", "101"]))).Updated.Should().Be(1);

        await run("NUMBERS", File(["Code", "Next number"], ["FSHM", "1042"]));
        (await context.Set<DocumentType>().AsNoTracking().SingleAsync(t => t.OwnerId == owner && t.Code == "FSHM", ct)).CodeCurrent.Should().Be(1042);
        await run("NUMBERS", File(["Code", "Parent", "Next number"], ["MP", "", "19"], ["MP", "FK", "5"]));
        (await context.Set<DocumentType>().AsNoTracking().Where(t => t.OwnerId == owner && t.Code == "MP").OrderBy(t => t.IdSup != null).Select(t => t.CodeCurrent).ToListAsync(ct))
            .Should().Equal([19, 5], "two types with one code are told apart by the one above");
        (await migration.ImportAsync("NUMBERS", File(["Code", "Next number"], ["FSHM", "1.5"]), "n.xlsx", false, ct)).Errors.Should().ContainSingle(e => e.Column == "Next number");
        (await migration.StepsAsync("en", ct)).Where(s => !s.Code!.StartsWith("OPENING", StringComparison.Ordinal)).Should().OnlyContain(s => s.Waits.Count == 0);
    }

    [Fact]
    public async Task Opening_balances_post_as_journals_on_the_opening_account_which_ends_at_zero_and_a_step_goes_in_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var (migration, context, _, owner, request) = await SetUpAsync(ct);
        await using var scope = context;
        var day = "2026-01-01";
        var run = async (string step, MemoryStream file, bool confirm = true) =>
        {
            var result = await migration.ImportAsync(step, file, "f.xlsx", confirm, ct);
            result.Errors.Should().BeEmpty(step);
            return result;
        };

        await run("PARTNERS", File(["Code", "Name", "Name (sq)", "Type", "Category"], ["C1", "Bar Lura", "Bar Lura", "LTD", "CLIENTS"], ["C2", "Euro Client", "Klient Euro", "LTD", "CLIENTS"], ["S1", "Furnitor", "Furnitor", "LTD", "SUPPLIERS"]));
        await run("PRODUCTS", File(["Code", "Name", "Name (sq)", "Type", "Category"], ["P1", "Water", "Ujë", "GOODS", await ItemCategoryAsync(context, ct)]));
        var money = await context.Set<Currency>().AsNoTracking().FirstAsync(c => c.OwnerId == owner && c.IsBase, ct);
        var euro = await context.Set<Currency>().AsNoTracking().FirstAsync(c => c.OwnerId == owner && !c.IsBase, ct);
        var bankAccount = await context.Set<Gamma.Erp.Models.Charts.Chart>().AsNoTracking().Where(c => c.OwnerId == owner && c.Code != null && c.Code.StartsWith("512")).OrderByDescending(c => c.Code).FirstAsync(ct);
        await run("COST_CENTERS", File(["Code", "Name", "Name (sq)", "Type", "Category", "Currency", "Account"], ["BK1", "Bank", "Banka", "BANK", Code(context.Set<CostCenterCategory>().AsNoTracking().First(c => c.IsElement)), money.Code!, bankAccount.Code!]));
        (await migration.StepsAsync("en", ct)).Single(s => s.Code == "OPENING_CHART").Waits.Should().NotBeEmpty("the chart comes after the detail");

        var opening = new[] { "Kind", "Code", "Debit", "Credit", "Currency", "Rate", "Date" };
        var partners = () => File(opening, ["CLIENT", "C1", "1000", "", "", "", day], ["CLIENT", "C2", "10", "", euro.Code!, "100", day], ["SUPPLIER", "S1", "", "600", "", "", day]);
        var check = await run("OPENING_PARTNERS", partners(), false);
        check.Saved.Should().BeFalse();
        check.OpeningBalance.Should().Be(-1400m, "the sheet balances on the opening account");
        context.ChangeTracker.Clear();
        (await context.Set<PartnerBalance>().CountAsync(ct)).Should().Be(0, "a check posts nothing");
        (await migration.OpeningAsync("sq", ct))!.Should().BeEquivalentTo(new { Account = "890", Balance = 0m });

        var wrongDay = await migration.ImportAsync("OPENING_PARTNERS", File(opening, ["CLIENT", "C1", "1000", "", "", "", day], ["SUPPLIER", "S1", "", "600", "", "", "2026-01-02"]), "f.xlsx", false, ct);
        wrongDay.Errors.Should().ContainSingle(e => e.Column == "Date");
        var bothSides = await migration.ImportAsync("OPENING_PARTNERS", File(opening, ["CLIENT", "C1", "5", "5", "", "", day]), "f.xlsx", false, ct);
        bothSides.Errors.Should().ContainSingle(e => e.Column == "Debit");
        var unknown = await migration.ImportAsync("OPENING_PARTNERS", File(opening, ["CLIENT", "NOPE", "5", "", "", "", day]), "f.xlsx", false, ct);
        unknown.Errors.Should().ContainSingle(e => e.Column == "Code");

        (await run("OPENING_PARTNERS", partners())).Saved.Should().BeTrue();
        context.ChangeTracker.Clear();
        var balances = await context.Set<PartnerBalance>().AsNoTracking().ToListAsync(ct);
        balances.Should().HaveCount(3);
        var c2 = (await context.Set<Partner>().AsNoTracking().SingleAsync(p => p.Code == "C2", ct)).Id;
        balances.Single(b => b.PartnerId == c2).Should().BeEquivalentTo(new { Debit = 10m, DebitLcu = 1000m }, o => o.ExcludingMissingMembers());
        (await migration.OpeningAsync("sq", ct))!.Balance.Should().Be(-1400m);

        var again = () => migration.ImportAsync("OPENING_PARTNERS", partners(), "f.xlsx", true, ct);
        (await again.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("step", "a step goes in once");

        using (var export = new XLWorkbook(new MemoryStream(await migration.ExportAsync("OPENING_PARTNERS", ct))))
        {
            var rows = export.Worksheet("Data").RowsUsed().Skip(1).ToList();
            rows.Should().HaveCount(3, "the balancing row is not the client's");
            rows.Single(r => r.Cell(2).GetString() == "C2").Cell(3).GetDouble().Should().Be(10, "in the row's own currency");
        }

        await run("OPENING_CASH", File(opening, ["BANK_ACCOUNT", "BK1", "5000", "", "", "", day]));
        await run("OPENING_STOCK", File(["Place", "Product", "Unit", "Quantity", "Unit cost", "Date"]));
        await run("OPENING_ASSETS", File(["Product", "Unit", "Book", "Value", "Start date", "Date"]));
        (await migration.OpeningAsync("sq", ct))!.Balance.Should().Be(-6400m);
        var chart = await run("OPENING_CHART", File(["Code", "Debit", "Credit", "Date"], ["101", "", "6400", day]));
        chart.OpeningBalance.Should().Be(0, "the detail agrees with the chart");
        (await migration.OpeningAsync("sq", ct))!.Balance.Should().Be(0);

        // Reversed, the step opens again.
        context.ChangeTracker.Clear();
        var sheet = (await context.Set<MigrationRun>().AsNoTracking().Where(r => r.DocumentId != null).OrderBy(r => r.CreatedOn).FirstAsync(ct)).DocumentId!.Value;
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        await documents.ReverseAsync(sheet, null, new DateOnly(2026, 1, 1), ct);
        context.ChangeTracker.Clear();
        (await run("OPENING_PARTNERS", partners())).Saved.Should().BeTrue("the reversal took the first file out");
        (await migration.OpeningAsync("sq", ct))!.Balance.Should().Be(0);
    }

    [Fact]
    public async Task The_chart_and_the_posting_groups_come_back_in_a_new_code_stays_on_its_statement_line_and_a_scenario_must_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        var (migration, context, _, owner, _) = await SetUpAsync(ct);
        await using var scope = context;

        var chart = await migration.ExportAsync("CHART", ct);
        var back = await migration.ImportAsync("CHART", new MemoryStream(chart), "chart.xlsx", false, ct);
        back.Errors.Should().BeEmpty();
        back.Added.Should().Be(0);
        using (var book = new XLWorkbook(new MemoryStream(chart)))
        {
            var headings = book.Worksheet("Data").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            headings.Should().Contain(["Name (sq)", "New code"]);
            (await migration.ImportAsync("CHART", File(["Code", "Name (sq)", "Type", "Category"], ["4119", "Klientë të tjerë", book.Worksheet("Data").RowsUsed().First(r => r.Cell(1).GetString() == "411").Cell(headings.IndexOf("Type") + 1).GetString(),
                book.Worksheet("Data").RowsUsed().First(r => r.Cell(1).GetString() == "411").Cell(headings.IndexOf("Category") + 1).GetString().Split(';')[0]]), "c.xlsx", false, ct)).Errors.Should().BeEmpty("the country's name is enough for an account");
        }

        var rename = (string code, string to) =>
        {
            using var book = new XLWorkbook(new MemoryStream(chart));
            var sheet = book.Worksheet("Data");
            var headings = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            var row = sheet.RowsUsed().First(r => r.Cell(1).GetString() == code);
            string Of(string heading) => row.Cell(headings.IndexOf(heading) + 1).GetString();
            return File(["Code", "Name (sq)", "Type", "Category", "New code"], [code, Of("Name (sq)"), Of("Type"), Of("Category"), to]);
        };
        var moved = await migration.ImportAsync("CHART", rename("411", "5999"), "c.xlsx", false, ct);
        moved.Errors.Should().ContainSingle(e => e.Message.Contains("statement line"), "statements read the start of the code");
        (await migration.ImportAsync("CHART", rename("411", "401"), "c.xlsx", false, ct)).Errors.Should().ContainSingle(e => e.Message.Contains("already exists"));
        (await migration.ImportAsync("CHART", rename("411", "4118"), "c.xlsx", true, ct)).Errors.Should().BeEmpty();
        var client = await context.Set<Gamma.Erp.Models.Charts.Chart>().AsNoTracking().SingleAsync(c => c.OwnerId == owner && c.Code == "4118", ct);
        (await context.Set<Gamma.Erp.Models.Posting.ScenarioConf>().AsNoTracking().CountAsync(l => l.ChartId == client.Id, ct)).Should().BeGreaterThan(0, "posting groups follow the account");
        (await migration.ImportAsync("CHART", rename("890", "8901"), "c.xlsx", true, ct)).Errors.Should().BeEmpty();
        (await context.Set<MigrationItem>().AsNoTracking().CountAsync(i => i.OffsetAccount == "8901", ct)).Should().Be(5, "the opening account follows its code");

        var groups = await migration.ExportAsync("POSTING_GROUPS", ct);
        var same = await migration.ImportAsync("POSTING_GROUPS", new MemoryStream(groups), "g.xlsx", true, ct);
        same.Errors.Should().BeEmpty();
        same.Saved.Should().BeTrue();
        var lines = await context.Set<Gamma.Erp.Models.Posting.ScenarioConf>().AsNoTracking().CountAsync(ct);
        (await context.Set<Gamma.Erp.Models.Posting.ScenarioConf>().IgnoreQueryFilters().AsNoTracking().CountAsync(ct)).Should().Be(lines, "a file that changes nothing writes nothing");

        using var edit = new XLWorkbook(new MemoryStream(groups));
        var sheet = edit.Worksheet("Data");
        var names = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        var row = sheet.RowsUsed().Skip(1).First(r => r.Cell(names.IndexOf("Account") + 1).GetString().Length > 0);
        var (document, line, covers, side) = (row.Cell(names.IndexOf("Document type") + 1).GetString(), row.Cell(names.IndexOf("Transaction type") + 1).GetString(), row.Cell(names.IndexOf("Scope") + 1).GetString(), row.Cell(names.IndexOf("Side") + 1).GetString());
        var parent = row.Cell(names.IndexOf("Document type parent") + 1).GetString();
        var scenario = sheet.RowsUsed().Skip(1).Where(r => r.Cell(names.IndexOf("Document type") + 1).GetString() == document && r.Cell(names.IndexOf("Document type parent") + 1).GetString() == parent
            && r.Cell(names.IndexOf("Transaction type") + 1).GetString() == line && r.Cell(names.IndexOf("Scope") + 1).GetString() == covers).ToList();
        row.Cell(names.IndexOf("Side") + 1).Value = side == "Debit" ? "Credit" : "Debit";
        var bad = new MemoryStream();
        edit.SaveAs(bad);
        bad.Position = 0;
        var refused = await migration.ImportAsync("POSTING_GROUPS", bad, "g.xlsx", true, ct);
        refused.Saved.Should().BeFalse();
        refused.Errors.Should().ContainSingle(e => e.Message.Contains("UnbalancedError"), "a scenario that can't balance would break every future document");
        scenario.Should().NotBeEmpty();
        context.ChangeTracker.Clear();
        (await context.Set<Gamma.Erp.Models.Posting.ScenarioConf>().AsNoTracking().CountAsync(ct)).Should().Be(lines, "nothing was saved");
        (await migration.ImportAsync("POSTING_GROUPS", File(["Document type", "Transaction type", "Scope", "Side", "Amount"], ["NOPE", "X", "Y", "Debit", "Transaction value"]), "g.xlsx", false, ct)).Errors.Should().ContainSingle(e => e.Column == "Scope");
    }

    [Fact]
    public async Task Document_categories_and_types_come_back_in_and_a_new_type_starts_here()
    {
        var ct = TestContext.Current.CancellationToken;
        var (migration, context, _, owner, _) = await SetUpAsync(ct);
        await using var scope = context;

        var categories = await migration.ImportAsync("DOCUMENT_CATEGORIES", new MemoryStream(await migration.ExportAsync("DOCUMENT_CATEGORIES", ct)), "c.xlsx", false, ct);
        categories.Errors.Should().BeEmpty();
        categories.Added.Should().Be(0);
        var types = await migration.ImportAsync("DOCUMENT_TYPES", new MemoryStream(await migration.ExportAsync("DOCUMENT_TYPES", ct)), "t.xlsx", false, ct);
        types.Errors.Should().BeEmpty("journal types share codes with top ones, and the parent tells them apart");
        types.Added.Should().Be(0);

        (await migration.ImportAsync("DOCUMENT_CATEGORIES", File(["Code", "Name", "Name (sq)"], ["CUSTOM", "Custom", "Të tjera"]), "c.xlsx", true, ct)).Errors.Should().BeEmpty();
        var head = new[] { "Code", "Name", "Name (sq)", "Category", "First number", "Last number", "Never posted", "Reversal", "Reverses" };
        (await migration.ImportAsync("DOCUMENT_TYPES", File(head, ["FXK", "Custom credit", "Kthim tjetër", "CUSTOM", "", "", "", "Yes", "FX"], ["FX", "Custom bill", "Faturë tjetër", "CUSTOM", "100", "999", "No", "No", ""]), "t.xlsx", false, ct))
            .Errors.Should().ContainSingle(e => e.Column == "Reverses", "the type it corrects must be there first, or earlier in the file");
        var added = await migration.ImportAsync("DOCUMENT_TYPES", File(head, ["FX", "Custom bill", "Faturë tjetër", "CUSTOM", "100", "999", "No", "No", ""]), "t.xlsx", true, ct);
        added.Errors.Should().BeEmpty();
        added.Added.Should().Be(1);
        var fx = await context.Set<DocumentType>().AsNoTracking().SingleAsync(t => t.Code == "FX", ct);
        (fx.CodeStart, fx.CodeEnd, fx.OwnerId.HasValue).Should().Be((100, 999, true));
        (await migration.ImportAsync("DOCUMENT_TYPES", File(head, ["FXK", "Custom credit", "Kthim tjetër", "CUSTOM", "", "", "", "Yes", "FX"]), "t.xlsx", true, ct)).Errors.Should().BeEmpty();
        var credit = await context.Set<DocumentType>().AsNoTracking().SingleAsync(t => t.Code == "FXK", ct);
        credit.Should().BeEquivalentTo(new { IsReversal = true, ReversedDocumentTypeId = fx.Id }, o => o.ExcludingMissingMembers());
    }

    [Fact]
    public void The_tree_comes_from_what_each_step_needs_and_refuses_a_loop()
    {
        MigrationItem Step(string code, string? needs, double order) => new() { Code = code, Requires = needs, SortOrder = order };
        var tree = MigrationTree.Build([Step("C", "A,B", 1), Step("B", "A", 3), Step("A", null, 2), Step("D", "C", 0)]);
        tree.Select(n => n.Item.Code).Should().Equal("A", "B", "C", "D");
        tree.Select(n => n.Level).Should().Equal(0, 1, 2, 3);
        tree[3].Above.Should().BeEquivalentTo(["A", "B", "C"], "a step waits for everything above it");
        tree[3].Needs.Should().Equal("C");

        var loop = () => MigrationTree.Build([Step("A", "B", 1), Step("B", "A", 2)]);
        loop.Should().Throw<ValidationException>().Which.Errors.Values.Single().Should().Contain("need each other");
        var unknown = () => MigrationTree.Build([Step("A", "NOPE", 1)]);
        unknown.Should().Throw<ValidationException>().Which.Errors.Values.Single().Should().Contain("NOPE");
    }

    [Fact]
    public async Task Line_types_and_line_kinds_come_back_in_and_a_new_scenario_starts_on_a_line_kind()
    {
        var ct = TestContext.Current.CancellationToken;
        var (migration, context, _, _, _) = await SetUpAsync(ct);
        await using var scope = context;
        foreach (var step in new[] { "LINE_CATEGORIES", "LINE_TYPES", "LINE_KINDS" })
        {
            var back = await migration.ImportAsync(step, new MemoryStream(await migration.ExportAsync(step, ct)), "f.xlsx", false, ct);
            back.Errors.Should().BeEmpty(step);
            back.Added.Should().Be(0, step);
        }

        (await migration.ImportAsync("DOCUMENT_TYPES", File(["Code", "Name", "Name (sq)", "Category"], ["FY", "Another bill", "Faturë tjetër", "SALES"]), "t.xlsx", true, ct)).Errors.Should().BeEmpty();
        (await migration.ImportAsync("LINE_KINDS", File(["Document type", "Transaction type"], ["FY", "ITEM"]), "k.xlsx", true, ct)).Should().BeEquivalentTo(new { Added = 1, Saved = true });
        var head = new[] { "Document type", "Transaction type", "Scope", "Account", "Side", "Amount", "Place", "Cost centre place" };
        var groups = await migration.ImportAsync("POSTING_GROUPS", File(head, ["FY", "ITEM", "articles", "35", "Debit", "Transaction value", "2", "2"], ["FY", "ITEM", "articles", "401", "Credit", "Transaction value", "1", "2"]), "g.xlsx", false, ct);
        groups.Errors.Should().NotBeEmpty().And.OnlyContain(e => e.Column == "Scope", "a scope is one in use");
        using var book = new XLWorkbook(new MemoryStream(await migration.ExportAsync("POSTING_GROUPS", ct)));
        var sheet = book.Worksheet("Data");
        var names = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        var used = sheet.RowsUsed().Skip(1).First(r => r.Cell(names.IndexOf("Transaction type") + 1).GetString() == "ITEM").Cell(names.IndexOf("Scope") + 1).GetString();
        var made = await migration.ImportAsync("POSTING_GROUPS", File(head, ["FY", "ITEM", used, "35", "Debit", "Transaction value", "2", "2"], ["FY", "ITEM", used, "401", "Credit", "Transaction value", "1", "2"]), "g.xlsx", true, ct);
        made.Errors.Should().BeEmpty();
        made.Saved.Should().BeTrue();
        (await context.Set<Gamma.Erp.Models.Posting.Scenario>().AsNoTracking().CountAsync(s => s.Name != null && s.CreatedOn >= DateTimeOffset.UtcNow.AddMinutes(-1) && s.ElementsId != null, ct)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Opening_stock_makes_its_cost_layers_and_fixed_assets_carry_on_from_their_depreciation()
    {
        var ct = TestContext.Current.CancellationToken;
        var (migration, context, _, owner, request) = await SetUpAsync(ct);
        await using var scope = context;
        var day = "2026-01-01";
        var run = async (string step, MemoryStream file) =>
        {
            var result = await migration.ImportAsync(step, file, "f.xlsx", true, ct);
            result.Errors.Should().BeEmpty(step);
            return result;
        };

        var unit = await context.Set<Unit>().AsNoTracking().OrderBy(u => u.Code).FirstAsync(ct);
        var category = await ItemCategoryAsync(context, ct);
        await run("PRODUCTS", File(["Code", "Name", "Name (sq)", "Type", "Category"], ["P1", "Water", "Ujë", "GOODS", category], ["V1", "Van", "Furgon", "GOODS", category]));
        var stock = new[] { "Place", "Product", "Unit", "Quantity", "Unit cost", "Date" };
        var file = () => File(stock, ["WH1", "P1", unit.Code!, "10", "100", day]);
        var result = await run("OPENING_STOCK", file());
        result.OpeningBalance.Should().Be(-1000m);
        context.ChangeTracker.Clear();
        var layer = await context.Set<ProductCostLayer>().AsNoTracking().SingleAsync(ct);
        layer.Should().BeEquivalentTo(new { QuantityLeft = 10m, ValueLeftLcu = 1000m, UnitCostLcu = 100m }, o => o.ExcludingMissingMembers());
        (await context.Set<InterCostCenterProductStock>().AsNoTracking().SumAsync(s => s.Quantity ?? 0, ct)).Should().Be(10);
        var inventory = await context.Set<Gamma.Erp.Models.Charts.ChartValue>().AsNoTracking().Where(v => v.Debit > 0).SumAsync(v => v.Debit ?? 0, ct);
        inventory.Should().Be(1000m);
        var twice = () => migration.ImportAsync("OPENING_STOCK", file(), "f.xlsx", true, ct);
        (await twice.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("step");

        var twenty = await context.Set<VatGroup>().AsNoTracking().FirstAsync(v => v.OwnerId == owner && v.Rate == 20, ct);
        (await run("PRODUCTS", File(["Code", "Name", "Name (sq)", "Type", "Category", "VAT group"], ["P2", "Tea", "Çaj", "GOODS", category, twenty.Name!.In("en")!]))).Added.Should().Be(1);
        (await context.Set<Product>().AsNoTracking().SingleAsync(p => p.Code == "P2", ct)).VatGroupId.Should().Be(twenty.Id);
        var offered = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var sale = await context.Set<DocumentType>().AsNoTracking().SingleAsync(t => t.OwnerId == owner && t.Code == "FSHM", ct);
        var p2 = (await context.Set<Product>().AsNoTracking().SingleAsync(p => p.Code == "P2", ct)).Id;
        var p1 = (await context.Set<Product>().AsNoTracking().SingleAsync(p => p.Code == "P1", ct)).Id;
        (await offered.LineProposalAsync(sale.Id, p2, null, null, null, "en", ct)).VatGroupId.Should().Be(twenty.Id, "the product's own group");
        (await offered.LineProposalAsync(sale.Id, p1, null, null, null, "en", ct)).VatGroupId.Should().BeNull("nothing named");
        var goods = await context.Set<ProductType>().SingleAsync(t => t.OwnerId == owner && t.Code == "GOODS", ct);
        goods.VatGroupId = twenty.Id;
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();
        (await offered.LineProposalAsync(sale.Id, p1, null, null, null, "en", ct)).VatGroupId.Should().Be(twenty.Id, "its type's group");
        var six = await context.Set<VatGroup>().AsNoTracking().FirstAsync(v => v.OwnerId == owner && v.Rate == 6, ct);
        (await run("DOCUMENT_VAT_GROUPS", File(["Document type", "VAT group"], ["FSHM", six.Name!.In("en")!]))).Saved.Should().BeTrue();
        (await offered.LineProposalAsync(sale.Id, p2, null, null, null, "en", ct)).VatGroupId.Should().BeNull("the document type doesn't offer it");

        var document = (await context.Set<MigrationRun>().AsNoTracking().SingleAsync(r => r.DocumentId != null, ct)).DocumentId!.Value;
        await new DocumentService(context, request, new Visibility(context, request), TimeProvider.System).ReverseAsync(document, null, new DateOnly(2026, 1, 1), ct);
        context.ChangeTracker.Clear();
        (await context.Set<InterCostCenterProductStock>().AsNoTracking().SumAsync(s => s.Quantity ?? 0, ct)).Should().Be(0, "a reversal takes the stock back out");
        (await run("OPENING_STOCK", file())).OpeningBalance.Should().Be(-1000m);

        var book = (await context.Set<Gamma.Erp.Models.Depreciation.DepreciationBook>().AsNoTracking().Where(b => b.OwnerId == owner && b.Posts).FirstAsync(ct)).Name!.In("en")!;
        var assets = new[] { "Product", "Unit", "Book", "Value", "Depreciated", "Start date", "Months", "Asset account", "Depreciation account", "Date" };
        (await migration.ImportAsync("OPENING_ASSETS", File(assets, ["V1", unit.Code!, book, "12000", "3000", "2025-01-01", "48", "", "", day]), "a.xlsx", false, ct)).Errors.Should().ContainSingle(e => e.Column == "Asset account");
        var opened = await run("OPENING_ASSETS", File(assets, ["V1", unit.Code!, book, "12000", "3000", "2025-01-01", "48", "215", "2805", day]));
        opened.OpeningBalance.Should().Be(-10000m, "stock 1000, and 12000 less 3000 of assets");
        context.ChangeTracker.Clear();
        var value = await context.Set<Gamma.Erp.Models.Depreciation.AssetValue>().AsNoTracking().SingleAsync(ct);
        value.InitialValue.Should().Be(12000m);
        var last = await context.Set<Gamma.Erp.Models.Depreciation.AssetDepreciation>().AsNoTracking().SingleAsync(ct);
        last.Should().BeEquivalentTo(new { BookValue = 9000m, Posted = true, DepreciationDate = new DateOnly(2026, 1, 1) }, o => o.ExcludingMissingMembers());
        using (var export = new XLWorkbook(new MemoryStream(await migration.ExportAsync("OPENING_ASSETS", ct))))
        {
            export.Worksheet("Data").RowsUsed().Skip(1).Single().CellsUsed().Select(c => c.GetString()).Should().Contain(["215", "2805"], "the accounts come back out");
        }

        var depreciation = new Gamma.Erp.Services.Depreciation.DepreciationService(context, request, new DocumentService(context, request, new Visibility(context, request), TimeProvider.System));
        var next = (await depreciation.CalculateAsync(value.BookId!.Value, new DateOnly(2026, 2, 1), ct)).Single();
        next.From.Should().Be(new DateOnly(2026, 1, 1), "a run carries on from the opening day");
    }

    [Fact]
    public async Task The_vat_lists_come_back_in_and_a_file_sets_what_it_names()
    {
        var ct = TestContext.Current.CancellationToken;
        var (migration, context, _, owner, _) = await SetUpAsync(ct);
        await using var scope = context;
        foreach (var step in new[] { "VAT_GROUPS", "DOCUMENT_VAT_GROUPS", "VAT_RETURN_BOXES", "VAT_RETURN_BOX_RULES" })
        {
            var back = await migration.ImportAsync(step, new MemoryStream(await migration.ExportAsync(step, ct)), "f.xlsx", true, ct);
            back.Errors.Should().BeEmpty(step);
            back.Added.Should().Be(0, step);
        }

        var groups = () => context.Set<VatGroup>().AsNoTracking().CountAsync(v => v.OwnerId == owner, ct);
        var before = await groups();
        var head = new[] { "Name", "Name (sq)", "Rate" };
        (await migration.ImportAsync("VAT_GROUPS", File(head, ["VAT 10%", "TVSH 10%", "10"]), "v.xlsx", true, ct)).Should().BeEquivalentTo(new { Added = 1, Saved = true });
        (await groups()).Should().Be(before + 1);
        (await migration.ImportAsync("VAT_GROUPS", File(head, ["VAT 10%", "TVSH 10%", "12"]), "v.xlsx", true, ct)).Updated.Should().Be(1, "a group with no code is matched by its name");
        (await migration.ImportAsync("VAT_GROUPS", File(head, ["VAT 9%", "TVSH 9%", "120"]), "v.xlsx", false, ct)).Errors.Should().ContainSingle(e => e.Column == "Rate");

        var fshm = await context.Set<DocumentType>().AsNoTracking().SingleAsync(t => t.OwnerId == owner && t.Code == "FSHM", ct);
        (await migration.ImportAsync("DOCUMENT_VAT_GROUPS", File(["Document type", "VAT group"], ["FSHM", "VAT 10%"]), "d.xlsx", true, ct)).Saved.Should().BeTrue();
        (await context.Set<InterDocumentTypeVatGroup>().AsNoTracking().CountAsync(l => l.DocumentTypeId == fshm.Id, ct)).Should().Be(1, "a file sets exactly the groups of the types it names");

        (await migration.ImportAsync("VAT_RETURN_BOXES", File(["Code", "Name", "Name (sq)", "Side", "Amount"], ["99", "Test box", "Kuti prove", "Sales", "VAT"]), "b.xlsx", true, ct)).Added.Should().Be(1);
        (await migration.ImportAsync("VAT_RETURN_BOXES", File(["Code", "Name", "Name (sq)", "Side", "Amount"], ["98", "X", "X", "Nowhere", "VAT"]), "b.xlsx", false, ct)).Errors.Should().ContainSingle(e => e.Column == "Side");
        (await migration.ImportAsync("VAT_RETURN_BOX_RULES", File(["Box", "Document category", "VAT group"], ["99", "SALES", "VAT 10%"]), "r.xlsx", true, ct)).Saved.Should().BeTrue();
        var box = await context.Set<VatReturnBox>().AsNoTracking().SingleAsync(b => b.Code == "99", ct);
        (await context.Set<VatReturnBoxRule>().AsNoTracking().CountAsync(r => r.BoxId == box.Id, ct)).Should().Be(1);
        (await migration.ImportAsync("VAT_RETURN_BOX_RULES", File(["Box", "Document category"], ["99", "NOPE"]), "r.xlsx", false, ct)).Errors.Should().ContainSingle(e => e.Column == "Document category");
    }

    [Fact]
    public async Task Stock_cost_has_no_vat_a_sale_posts_its_cost_a_reversal_puts_it_back_and_a_closed_period_refuses()
    {
        var ct = TestContext.Current.CancellationToken;
        var (migration, context, _, owner, request) = await SetUpAsync(ct);
        await using var scope = context;
        var run = async (string step, MemoryStream file) => (await migration.ImportAsync(step, file, "f.xlsx", true, ct)).Errors.Should().BeEmpty(step);
        await run("PARTNERS", File(["Code", "Name", "Name (sq)", "Type", "Category"], ["C1", "Client", "Klient", "LTD", "CLIENTS"], ["S1", "Supplier", "Furnitor", "LTD", "SUPPLIERS"]));
        await run("PRODUCTS", File(["Code", "Name", "Name (sq)", "Type", "Category"], ["P1", "Water", "Ujë", "GOODS", await ItemCategoryAsync(context, ct)]));
        var product = await context.Set<Product>().AsNoTracking().SingleAsync(p => p.Code == "P1", ct);
        var unit = await context.Set<Unit>().AsNoTracking().OrderBy(u => u.Code).FirstAsync(ct);
        var warehouse = await context.Set<CostCenter>().AsNoTracking().SingleAsync(c => c.Code == "WH1", ct);
        var client = await context.Set<Partner>().AsNoTracking().SingleAsync(p => p.Code == "C1", ct);
        var supplier = await context.Set<Partner>().AsNoTracking().SingleAsync(p => p.Code == "S1", ct);
        var vat = await context.Set<VatGroup>().AsNoTracking().FirstAsync(v => v.OwnerId == owner && v.Rate == 20, ct);
        var item = await context.Set<Gamma.Erp.Models.Transactions.TransactionType>().AsNoTracking().FirstAsync(t => t.Code == "ITEM", ct);
        var conf = await context.Set<Gamma.Erp.Models.Transactions.TransactionTypeConf>().AsNoTracking().FirstAsync(c => c.TransactionTypeId == item.Id, ct);
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        async Task<Guid> Post(string code, Guid from, Guid to, decimal quantity, decimal price, DateOnly day)
        {
            var type = await context.Set<DocumentType>().AsNoTracking().SingleAsync(t => t.OwnerId == owner && t.Code == code, ct);
            var category = await context.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == type.Id).Select(l => l.CategoryId).FirstAsync(ct);
            var saved = await documents.SaveAsync(new Gamma.Erp.Models.Documents.Dto.DocumentDto
            {
                TypeId = type.Id, CategoryIds = [category], PostingDate = day, DocumentDate = day, FromId = from, ToId = to,
                Transactions = [new Gamma.Erp.Models.Transactions.Dto.TransactionDto { TypeId = item.Id, TypeConfId = conf.Id, ElementId = product.Id, ElementTypeId = product.TypeId, ElementName = "Water", UnitId = unit.Id, Quantity = quantity, Price = price, VatGroupId = vat.Id }],
            }, ct);
            var posted = await documents.PostAsync(saved.Id!.Value, ct);
            context.ChangeTracker.Clear();
            return posted.Id!.Value;
        }

        async Task<decimal> Balance(string account) => await (from v in context.Set<Gamma.Erp.Models.Charts.ChartValue>().AsNoTracking()
                                                              join c in context.Set<Gamma.Erp.Models.Charts.Chart>().AsNoTracking() on v.ChartId equals c.Id
                                                              where c.Code == account && c.OwnerId == owner
                                                              select (v.Debit ?? 0) - (v.Credit ?? 0)).SumAsync(ct);

        await Post("FBM", supplier.Id, warehouse.Id, 10, 100, new DateOnly(2026, 2, 1));
        (await context.Set<ProductCostLayer>().AsNoTracking().SingleAsync(ct)).Should().BeEquivalentTo(new { UnitCostLcu = 100m, ValueLeftLcu = 1000m }, o => o.ExcludingMissingMembers(), "a purchase's stock has no VAT in it");
        (await Balance("35")).Should().Be(1000m);

        var sale = await Post("FSHM", warehouse.Id, client.Id, 4, 150, new DateOnly(2026, 2, 2));
        (await Balance("605")).Should().Be(400m, "a sale posts what its goods cost");
        (await Balance("35")).Should().Be(600m, "and they leave the accounts");
        (await context.Set<ProductCostLayer>().AsNoTracking().SumAsync(l => l.ValueLeftLcu, ct)).Should().Be(600m, "the accounts and the layers agree");

        await documents.ReverseAsync(sale, null, new DateOnly(2026, 2, 3), ct);
        context.ChangeTracker.Clear();
        (await Balance("605")).Should().Be(0m, "a reversal puts the cost back");
        (await Balance("35")).Should().Be(1000m);

        context.Add(new PostingLock { LockedUntil = new DateOnly(2026, 2, 28), OwnerId = owner, TenantId = request.TenantId });
        await context.SaveChangesAsync(ct);
        var closed = () => Post("FSHM", warehouse.Id, client.Id, 1, 150, new DateOnly(2026, 2, 20));
        (await closed.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("postingDate");
        context.ChangeTracker.Clear();
        await Post("FSHM", warehouse.Id, client.Id, 1, 150, new DateOnly(2026, 3, 1));
        (await Balance("605")).Should().Be(100m, "a day after the closed period posts");
    }

    [Fact]
    public async Task Menu_access_opens_it_and_the_server_refuses_anyone_else()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, context, _, _, request) = await SetUpAsync(ct);
        await using var scope = context;
        var refused = () => Service(context, request with { IsAdministrator = false }).ExportAsync("PARTNERS", ct);
        (await refused.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("rights");
    }

    private static string Code(Category category) => category.Code!;

    // The category an item line takes its products from.
    private static async Task<string> ItemCategoryAsync(ErpDbContext context, CancellationToken ct) =>
        await (from c in context.Set<Gamma.Erp.Models.Transactions.TransactionTypeConf>().AsNoTracking()
               join t in context.Set<Gamma.Erp.Models.Transactions.TransactionType>().AsNoTracking() on c.TransactionTypeId equals t.Id
               join e in context.Set<Elements>().AsNoTracking() on c.ElementsId equals e.Id
               join k in context.Set<ProductCategory>().AsNoTracking() on e.CategoryId equals k.Id
               where t.Code == "ITEM"
               select k.Code!).FirstAsync(ct);

    private static string TypeOf(ErpDbContext context) => context.Set<UnitType>().AsNoTracking().First(t => t.IsElement).Code!;

    private static string UnitCategoryOf(ErpDbContext context) => context.Set<UnitCategory>().AsNoTracking().First(c => c.IsElement).Code!;
}

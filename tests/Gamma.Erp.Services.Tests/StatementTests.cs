using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>The balance sheet and profit and loss (the founder, 2026-11-01): lines from configuration, a date that counts, totals at every level, no account lost.</summary>
public sealed class StatementTests : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 6, 30);
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
    public async Task The_statements_follow_their_lines_balance_and_lose_no_account()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", [("cost_center_type:OFFICE", "HQ", "Zyra")]);
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
        var request = new RequestContext(tenantId, null, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        var visibility = new Visibility(context, request);
        var documents = new DocumentService(context, request, visibility, TimeProvider.System);
        var reports = new ReportService(context, visibility);
        var types = await context.Set<DocumentType>().ToListAsync(ct);
        var entry = types.Single(t => t.Code == "GJ");
        var sheetType = types.Single(t => t.Id == entry.IdSup);
        var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == sheetType.Id).Select(l => l.CategoryId).FirstAsync(ct);
        var charts = await context.Set<Chart>().ToListAsync(ct);
        var parents = charts.Where(c => c.IdSup != null).Select(c => c.IdSup!.Value).ToHashSet();
        Chart Leaf(string start) => charts.Where(c => c.Code!.StartsWith(start, StringComparison.Ordinal) && !parents.Contains(c.Id)).OrderBy(c => c.Code, StringComparer.Ordinal).First();
        var line = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT", ct)).Id;
        var vat0 = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 0, ct)).Id;
        var office = (await context.Set<CostCenter>().SingleAsync(c => c.Code == "HQ", ct)).Id;
        DocumentDto Row(Chart account, decimal debit, decimal credit) => new()
        {
            TypeId = entry.Id, FromId = office, ToId = office,
            Transactions = [new TransactionDto { TypeId = line, ElementId = account.Id, ElementTypeId = account.TypeId, ElementName = account.Code, Price = debit + credit, Debit = debit, Credit = credit, VatGroupId = vat0 }],
        };
        async Task PostAsync(DateOnly day, params DocumentDto[] rows)
        {
            var sheet = await documents.SaveSheetAsync(new SheetDto(new DocumentDto { TypeId = sheetType.Id, CategoryIds = [category], DocumentDate = day, PostingDate = day }, rows), ct);
            await documents.PostAsync(sheet.Sheet.Id!.Value, ct);
            context.ChangeTracker.Clear();
        }

        var (cash, capital, payable, receivable, income, expense) = (Leaf("5311"), Leaf("10"), Leaf("401"), Leaf("411"), Leaf("705"), Leaf("6132"));
        await PostAsync(new DateOnly(2025, 3, 1), Row(cash, 1000, 0), Row(capital, 0, 1000));
        await PostAsync(new DateOnly(2025, 6, 1), Row(receivable, 80, 0), Row(income, 0, 80));
        await PostAsync(Day, Row(receivable, 300, 0), Row(income, 0, 300), Row(expense, 200, 0), Row(payable, 0, 200));
        await PostAsync(Day.AddDays(1), Row(cash, 999, 0), Row(capital, 0, 999));

        // At the day: nothing after it, and the two sides meet. Cash 1000 + clients 380 = capital 1000 + earlier 80 + this year 100 + suppliers 200.
        var sheetNow = await reports.BalanceSheetAsync(Day, "en", ct);
        decimal Amount(string code) => sheetNow.Rows.Single(r => r.Code == code).Amount;
        (sheetNow.Assets, sheetNow.LiabilitiesAndEquity, sheetNow.Difference).Should().Be((1380m, 1380m, 0m));
        (Amount("A23"), Amount("A22"), Amount("P11"), Amount("P12"), Amount("P13"), Amount("P21")).Should().Be((1000m, 380m, 1000m, 80m, 100m, 200m));
        (Amount("A"), Amount("A2"), Amount("P"), Amount("P1")).Should().Be((1380m, 1380m, 1380m, 1180m), "a heading totals every level below it");
        sheetNow.Rows.Single(r => r.Code == "A23").Level.Should().Be(2);
        sheetNow.Rows.Single(r => r.Code == "A23").Previous.Should().Be(1000, "a year before");
        sheetNow.Unplaced.Should().BeEmpty();

        // A client in credit moves to the liabilities, by the balance rule.
        await PostAsync(Day.AddDays(2), Row(cash, 500, 0), Row(receivable, 0, 500));
        var turned = await reports.BalanceSheetAsync(Day.AddDays(2), "en", ct);
        turned.Rows.Single(r => r.Code == "A22").Amount.Should().Be(0);
        turned.Rows.Single(r => r.Code == "P21").Amount.Should().Be(320, "suppliers 200 + the client's 120");
        turned.Difference.Should().Be(0);

        // Profit and loss of a period, the same period a year before beside it.
        var pl = await reports.ProfitLossAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "en", ct);
        (pl.Rows.Single(r => r.Code == "I").Amount, pl.Rows.Single(r => r.Code == "E").Amount, pl.Result, pl.PreviousResult).Should().Be((300m, 200m, 100m, 80m));

        // An account on no line is listed, not dropped: the off-balance class has none.
        var off = Leaf("8");
        await PostAsync(Day, Row(off, 40, 0), Row(capital, 0, 40));
        var listed = await reports.BalanceSheetAsync(Day, "en", ct);
        listed.Unplaced.Should().ContainSingle().Which.Code.Should().Be(off.Code);
        listed.Difference.Should().Be(-40, "the check shows what is missing");
    }
}

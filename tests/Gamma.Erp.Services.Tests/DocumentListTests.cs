using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Users;
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

/// <summary>
/// The document form and lists: every column filtered and sorted, previous / next, the typed invoice number, journal line references,
/// "show scenario" before posting, a cost centre's balance and the documents behind it, the reports for one cost centre.
/// </summary>
public sealed class DocumentListTests : IAsyncLifetime
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
    public async Task A_list_is_filtered_and_sorted_by_any_column_the_form_steps_and_previews_and_a_cost_centre_shows_its_balance()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", [("cost_center_type:WAREHOUSE", "WH", "Magazina"), ("cost_center_type:CASH_DESK", "CASH", "Arka")]);
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
        await using var context = Context(tenant);
        var client = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id, Code = "C1", Name = TranslatedText.English("Bar Lura"),
            TenantId = tenantId, OwnerId = company.Corporate };
        context.Add(client);
        await context.SaveChangesAsync(ct);
        var warehouse = await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH", ct);
        var cash = await context.Set<CostCenter>().SingleAsync(c => c.Code == "CASH", ct);
        var accounts = await context.Set<Chart>().ToDictionaryAsync(c => c.Code!, ct);
        var lineType = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT", ct)).Id;
        var vat20 = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id;
        var vat0 = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 0, ct)).Id;
        var sales = await context.Set<DocumentCategory>().SingleAsync(c => c.Code == "SALES", ct);
        var inSales = context.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == sales.Id).Select(l => l.TypeId);
        var sale = await context.Set<DocumentType>().SingleAsync(t => t.Code == "FSHM" && inSales.Contains(t.Id), ct);
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var date = new DateOnly(2026, 10, 30);
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);

        async Task<DocumentDto> SaleAsync(decimal price, string? external = null, string? reference = null) => await documents.SaveAsync(new DocumentDto
        {
            TypeId = sale.Id, CategoryIds = [sales.Id], DocumentDate = date, PostingDate = date, FromId = warehouse.Id, FromName = "Magazina", ToId = client.Id, ToName = "Bar Lura",
            ExternalNumber = external,
            Transactions = [new TransactionDto { TypeId = lineType, ElementId = accounts["705"].Id, ElementTypeId = accounts["705"].TypeId, ElementName = "Mallra", Price = price,
                VatGroupId = vat20, ReferenceNumber = reference, ReferenceDocumentTypeId = reference is null ? null : sale.Id }],
        }, ct);

        var first = await SaleAsync(100);
        var second = await SaleAsync(300, external: " INV-77 ", reference: "A-12");
        var third = await SaleAsync(200);

        // "Show scenario": the entries posting would write, before posting, the same posting then writes.
        var preview = (await documents.PostingPreviewAsync(second.Id!.Value, ct))!;
        preview.Sum(r => r.Debit).Should().Be(preview.Sum(r => r.Credit)).And.Be(360);
        preview.Should().Contain(r => r.Account == "705" && r.Credit == 300).And.Contain(r => r.Account == "411" && r.Debit == 360);
        (await context.Set<ChartValue>().CountAsync(v => v.DocumentId == second.Id, ct)).Should().Be(0, "a preview writes nothing");
        await documents.PostAsync(second.Id!.Value, ct);
        await documents.PostAsync(third.Id!.Value, ct);
        (await context.Set<ChartValue>().Where(v => v.DocumentId == second.Id).Select(v => new { v.ChartId, Debit = v.Debit ?? 0, Credit = v.Credit ?? 0 }).ToListAsync(ct))
            .Select(v => (v.ChartId, v.Debit, v.Credit)).Should().BeEquivalentTo(preview.Select(r => ((Guid?)r.ChartId, r.Debit, r.Credit)));

        // The typed number and the journal line's reference come back as typed (trimmed).
        var read = (await documents.DocumentAsync(second.Id!.Value, ct))!;
        read.ExternalNumber.Should().Be("INV-77");
        (read.Transactions[0].ReferenceNumber, read.Transactions[0].ReferenceDocumentTypeId).Should().Be(("A-12", sale.Id));

        // The list: every column filtered, sorted any way; by default the newest first.
        (await documents.ListAsync(new DocumentQuery(sales.Id), ct)).Items.Select(d => d.Id).Should().Equal(third.Id!.Value, second.Id!.Value, first.Id!.Value);
        (await documents.ListAsync(new DocumentQuery(sales.Id, Sort: "total"), ct)).Items.Select(d => d.Total).Should().Equal(120, 240, 360);
        (await documents.ListAsync(new DocumentQuery(sales.Id, Sort: "total", Descending: true), ct)).Items[0].Id.Should().Be(second.Id!.Value);
        (await documents.ListAsync(new DocumentQuery(sales.Id, Posted: true), ct)).Total.Should().Be(2);
        (await documents.ListAsync(new DocumentQuery(sales.Id, ExternalNumber: "inv-7"), ct)).Items.Should().ContainSingle().Which.Id.Should().Be(second.Id!.Value);
        (await documents.ListAsync(new DocumentQuery(sales.Id, To: "lura"), ct)).Total.Should().Be(3, "a column filter matches its text anywhere, case aside");
        (await documents.ListAsync(new DocumentQuery(sales.Id, To: "100%"), ct)).Total.Should().Be(0, "the filter's text is taken as it is, never as a pattern");
        (await documents.ListAsync(new DocumentQuery(sales.Id, TotalFrom: 200, TotalTo: 300), ct)).Items.Should().ContainSingle().Which.Total.Should().Be(240);
        (await documents.ListAsync(new DocumentQuery(sales.Id, CreatedBy: user, CreatedFrom: before), ct)).Total.Should().Be(3, "the POS sales list per waiter and time");
        (await documents.ListAsync(new DocumentQuery(sales.Id, CreatedBy: Guid.CreateVersion7()), ct)).Total.Should().Be(0);
        var refused = async () => await documents.ListAsync(new DocumentQuery(sales.Id, Sort: "password"), ct);
        await refused.Should().ThrowAsync<ValidationException>();

        // ← and → on the form: the documents of the same type made before and after.
        (await documents.NeighboursAsync(second.Id!.Value, ct)).Should().Be(new DocumentNeighboursDto(first.Id, third.Id));
        (await documents.NeighboursAsync(first.Id!.Value, ct))!.Previous.Should().BeNull();

        // A cost centre's balance and the documents behind it; the reports for that cost centre alone.
        var cashCategory = await context.Set<DocumentCategory>().SingleAsync(c => c.Code == "CASH_DESK", ct);
        var inCash = context.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == cashCategory.Id).Select(l => l.TypeId);
        var receiptType = await context.Set<DocumentType>().SingleAsync(t => t.Code == "MA" && inCash.Contains(t.Id), ct);
        var receipt = await documents.SaveAsync(new DocumentDto
        {
            TypeId = receiptType.Id, CategoryIds = [cashCategory.Id], DocumentDate = date, PostingDate = date, FromId = client.Id, FromName = "Bar Lura", ToId = cash.Id, ToName = "Arka",
            Transactions = [new TransactionDto { TypeId = lineType, ElementId = accounts["411"].Id, ElementTypeId = accounts["411"].TypeId, ElementName = "Klienti", Price = 500,
                VatGroupId = vat0 }],
        }, ct);
        await documents.PostAsync(receipt.Id!.Value, ct);
        var reports = new ReportService(context, new Visibility(context, request));
        var balance = (await reports.CostCenterBalanceAsync(cash.Id, null, ct))!;
        var kept = await context.Set<CostCenterBalance>().Where(b => b.CostCenterId == cash.Id && b.Active).SumAsync(b => (b.DebitLcu ?? 0) - (b.CreditLcu ?? 0), ct);
        balance.Sum(b => b.Balance).Should().Be(kept).And.NotBe(0, "the receipt brought money into the cash desk");
        var ledger = (await reports.CostCenterLedgerAsync(cash.Id, date, date, ct))!;
        ledger.Should().HaveCount(2, "the balance brought forward, then the receipt");
        (ledger[1].Document, ledger[1].Balance).Should().Be((receipt.Code, kept));
        (await reports.CostCenterBalanceAsync(Guid.CreateVersion7(), null, ct)).Should().BeNull();
        var cashOnly = await reports.TrialBalanceAsync(date, date, null, "sq", cash.Id, ct);
        (cashOnly.TotalDebit, cashOnly.TotalCredit).Should().Be((500m, 500m), "only the entries carrying the cash desk: the receipt's");
        (await reports.LedgerAsync(accounts["5311"].Id, date, date, "sq", cash.Id, ct))!.Closing.Should().Be(500);
    }
}

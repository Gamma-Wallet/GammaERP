using System.Text.Json.Nodes;
using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Posting;
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
/// The reports of an Albanian company with a September sale, an October sale reversed in full and an October purchase: the trial balance
/// summed up the chart's tree and balanced, an account's ledger with its opening balance, the sales and purchase VAT books, the VAT return.
/// </summary>
public sealed class ReportTests : IAsyncLifetime
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
    public async Task The_reports_read_the_posted_entries_by_period_summed_up_the_chart_and_the_VAT_books_count_a_reversal_negative()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", [("cost_center_type:WAREHOUSE", "WH", "Magazina")]);
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
        var supplier = new Partner { TypeId = ltd, Code = "S1", Name = TranslatedText.English("Furnizuesi"), TenantId = tenantId, OwnerId = company.Corporate };
        context.AddRange(client, supplier);
        var properties = await context.Set<PartnerTypeInfo>().Where(t => t.TypeId == ltd).ToListAsync(ct);
        PartnerInfo Detail(string property, string value) => new()
        {
            ElementId = client.Id, TypeInfoId = properties.Single(t => t.Name.In("en") == property).Id, Value = new JsonObject { ["sq"] = value }, TenantId = tenantId, OwnerId = company.Corporate,
        };
        context.AddRange(Detail(ReportService.TaxNumberProperty, "K12345678A"), Detail(ReportService.CityProperty, " Tiranë "));
        await context.SaveChangesAsync(ct);

        var accounts = await context.Set<Chart>().ToDictionaryAsync(c => c.Code!, ct);
        var lineType = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT", ct)).Id;
        var vat20 = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id;
        var warehouse = await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH", ct);
        var visibility = new Visibility(context, request);
        var documents = new DocumentService(context, request, visibility, TimeProvider.System);
        async Task<(Guid Category, Guid Type)> TypeAsync(string category, string code)
        {
            var c = await context.Set<DocumentCategory>().SingleAsync(x => x.Code == category, ct);
            var inCategory = context.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == c.Id).Select(l => l.TypeId);
            return (c.Id, (await context.Set<DocumentType>().SingleAsync(t => t.Code == code && inCategory.Contains(t.Id), ct)).Id);
        }

        async Task<DocumentDto> PostAsync((Guid Category, Guid Type) type, DateOnly date, (Guid Id, string Name) from, (Guid Id, string Name) to, string account, decimal price)
        {
            var saved = await documents.SaveAsync(new DocumentDto
            {
                TypeId = type.Type, CategoryIds = [type.Category], DocumentDate = date, PostingDate = date, FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name,
                ExternalNumber = $"P-{price:0}",
                Transactions = [new TransactionDto { TypeId = lineType, ElementId = accounts[account].Id, ElementTypeId = accounts[account].TypeId, ElementName = account, Price = price,
                    VatGroupId = vat20 }],
            }, ct);
            return await documents.PostAsync(saved.Id!.Value, ct);
        }

        var sale = await TypeAsync("SALES", "FSHM");
        var purchase = await TypeAsync("PURCHASES", "FBM");
        await PostAsync(sale, new DateOnly(2026, 9, 15), (warehouse.Id, "Magazina"), (client.Id, "Bar Lura"), "705", 200);
        var october = await PostAsync(sale, new DateOnly(2026, 10, 10), (warehouse.Id, "Magazina"), (client.Id, "Bar Lura"), "705", 1000);
        await documents.ReverseAsync(october.Id!.Value, null, new DateOnly(2026, 10, 20), ct);
        await PostAsync(purchase, new DateOnly(2026, 10, 12), (supplier.Id, "Furnizuesi"), (warehouse.Id, "Magazina"), "6011", 500);

        var reports = new ReportService(context, visibility);
        var (from, to) = (new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var trial = await reports.TrialBalanceAsync(from, to, null, "sq", null, ct);
        trial.TotalDebit.Should().Be(trial.TotalCredit, "every posting balances, so the trial balance does");
        var receivable = trial.Rows.Single(r => r.Code == "411");
        (receivable.OpeningDebit, receivable.Debit, receivable.Credit, receivable.ClosingDebit).Should().Be((240m, 1200m, 1200m, 240m),
            "September's sale before the period; October's sale and its reversal in it");
        var income = trial.Rows.Single(r => r.Code == "705");
        var incomeClass = trial.Rows.Single(r => r.AccountId == accounts["705"].IdSup);
        (incomeClass.IsGroup, incomeClass.Debit, incomeClass.Credit, incomeClass.OpeningCredit).Should().Be((true, income.Debit, income.Credit, income.OpeningCredit),
            "a group sums every account under it");
        trial.Rows.Should().NotContain(r => r.Code == "5311", "an account with nothing on it is left out");

        var ledger = (await reports.LedgerAsync(accounts["411"].Id, from, to, "sq", null, ct))!;
        (ledger.Opening, ledger.Lines.Count, ledger.Closing).Should().Be((240m, 2, 240m));
        ledger.Lines[^1].Balance.Should().Be(240);

        var book = await reports.VatRegisterAsync([sale.Category], from, to, "sq", ct);
        book.Lines.Should().HaveCount(2);
        book.Lines.Single(l => l.Reversal).Vat.Should().Be(-200, "the reversal (of the sale's reversal type, in the Reversals category) counts negative");
        book.Lines.Single(l => !l.Reversal).Partner.Should().Be("Bar Lura");
        (book.Base, book.Vat).Should().Be((0m, 0m));
        var line = book.Lines.Single(l => !l.Reversal);
        (line.ExternalNumber, line.CostCenter, line.TaxNumber, line.City).Should().Be(("P-1000", "Magazina", "K12345678A", "Tiranë"));
        var bought = (await reports.VatRegisterAsync([purchase.Category], from, to, "sq", ct)).Lines.Should().ContainSingle().Which;
        (bought.ExternalNumber, bought.CostCenter, bought.Partner, bought.TaxNumber, bought.City).Should().Be(("P-500", "Magazina", "Furnizuesi", null, null), "a property left empty stays empty");
        (await reports.VatRegisterAsync([sale.Category], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "sq", ct)).Vat.Should().Be(40);

        var vatReturn = await reports.VatReturnAsync([sale.Category], [purchase.Category], from, to, "sq", ct);
        (vatReturn.OutputVat, vatReturn.InputVat, vatReturn.Payable).Should().Be((0m, 100m, -100m), "October's input VAT is to be refunded");
        vatReturn.Input.Should().ContainSingle().Which.Base.Should().Be(500);

        // A business that buys nothing with VAT names only its sales; with neither side named there is nothing to return.
        var salesOnly = await reports.VatReturnAsync([sale.Category], [], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "sq", ct);
        (salesOnly.OutputVat, salesOnly.InputVat, salesOnly.Payable).Should().Be((40m, 0m, 40m));
        salesOnly.Input.Should().BeEmpty();
        var nothing = async () => await reports.VatReturnAsync([], [], from, to, "sq", ct);
        await nothing.Should().ThrowAsync<ValidationException>();
    }
}

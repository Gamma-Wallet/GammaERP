using System.Text.Json.Nodes;
using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Currencies;
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
using Gamma.Erp.Services.Currencies;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>Exchange rates by day (a document takes its day's rate when none is typed) and the fiscal year's cumulative trial balance (H2).</summary>
public sealed class CurrencyTests : IAsyncLifetime
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
    public async Task A_document_takes_its_days_rate_and_the_trial_balance_sums_the_fiscal_year_from_the_companys_month()
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
        var visibility = new Visibility(context, request);
        var currencies = new CurrencyService(context, request, visibility);
        var list = await currencies.CurrenciesAsync("en", ct);
        list[0].Code.Should().Be("ALL", "the base currency first");
        var euro = list.Single(c => c.Code == "EUR").Id;

        await currencies.SetRateAsync(new SetRateRequest(euro, new DateOnly(2026, 10, 1), 100), ct);
        await currencies.SetRateAsync(new SetRateRequest(euro, new DateOnly(2026, 10, 15), 99), ct);
        await currencies.SetRateAsync(new SetRateRequest(euro, new DateOnly(2026, 10, 15), 98), ct);
        (await currencies.RatesAsync(euro, null, null, ct)).Select(r => (r.RateDate.Day, r.Rate)).Should().Equal((1, 100m), (15, 98m));
        (await currencies.RateAsync(euro, new DateOnly(2026, 10, 10), ct)).Should().Be(100, "the latest rate on or before the day");
        (await currencies.RateAsync(euro, new DateOnly(2026, 9, 30), ct)).Should().BeNull("no rate yet");
        var baseRate = async () => await currencies.SetRateAsync(new SetRateRequest(list[0].Id, new DateOnly(2026, 10, 1), 2), ct);
        await baseRate.Should().ThrowAsync<ValidationException>();

        // A currency added, renamed, its code one in a company and three letters; the company's own stays.
        var krone = await currencies.SaveCurrencyAsync(new SaveCurrencyRequest(null, " nok ", "Krone"), ct);
        (krone.Code, krone.Name, krone.IsBase).Should().Be(("NOK", "Krone", false));
        (await currencies.SaveCurrencyAsync(new SaveCurrencyRequest(krone.Id, "XXX", "Norwegian krone"), ct)).Should().Be(krone with { Name = "Norwegian krone" }, "a code stays");
        foreach (var refused in new SaveCurrencyRequest[] { new(null, "NOK", null), new(null, "NO", null), new(null, "N0K", null) })
        {
            var again = async () => await currencies.SaveCurrencyAsync(refused, ct);
            await again.Should().ThrowAsync<ValidationException>();
        }

        var own = async () => await currencies.SwitchOffCurrencyAsync(list[0].Id, ct);
        await own.Should().ThrowAsync<ValidationException>();
        await currencies.SetRateAsync(new SetRateRequest(krone.Id, new DateOnly(2026, 10, 1), 9), ct);
        await currencies.SwitchOffCurrencyAsync(krone.Id, ct);
        (await currencies.CurrenciesAsync("en", ct)).Should().NotContain(c => c.Code == "NOK", "a rate alone does not keep it");
        (await currencies.SaveCurrencyAsync(new SaveCurrencyRequest(null, "NOK", null), ct)).Name.Should().Be("NOK", "its code is free again");

        var client = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id, Code = "C1", Name = TranslatedText.English("Client"),
            TenantId = tenantId, OwnerId = company.Corporate };
        context.Add(client);
        await context.SaveChangesAsync(ct);
        var warehouse = await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH", ct);
        var income = await context.Set<Chart>().SingleAsync(c => c.Code == "705", ct);
        var lineType = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT", ct)).Id;
        var vat20 = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id;
        var sales = await context.Set<DocumentCategory>().SingleAsync(c => c.Code == "SALES", ct);
        var inSales = context.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == sales.Id).Select(l => l.TypeId);
        var sale = await context.Set<DocumentType>().SingleAsync(t => t.Code == "FSHM" && inSales.Contains(t.Id), ct);
        var documents = new DocumentService(context, request, visibility, TimeProvider.System);
        async Task<DocumentDto> SellAsync(DateOnly date, decimal price, Guid? currency = null) => await documents.PostAsync((await documents.SaveAsync(new DocumentDto
        {
            TypeId = sale.Id, CategoryIds = [sales.Id], DocumentDate = date, PostingDate = date, FromId = warehouse.Id, FromName = "Magazina", ToId = client.Id, ToName = "Client",
            Transactions = [new TransactionDto { TypeId = lineType, ElementId = income.Id, ElementTypeId = income.TypeId, ElementName = "Goods", Price = price, VatGroupId = vat20,
                CurrencyId = currency }],
        }, ct)).Id!.Value, ct);

        var inEuro = await SellAsync(new DateOnly(2026, 10, 20), 10, euro);
        (inEuro.Transactions[0].ExchangeRate, inEuro.Transactions[0].ValueLcu).Should().Be((98m, 980m), "no rate typed: the day's rate");
        var used = async () => await currencies.SwitchOffCurrencyAsync(euro, ct);
        (await used.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain("used on a document");
        var listed = (await documents.ListAsync(new DocumentQuery(sales.Id), ct)).Items.Should().ContainSingle(i => i.Id == inEuro.Id).Which;
        (listed.CurrencyCode, listed.ExchangeRate).Should().Be(("EUR", 98m));
        var noRate = async () => await SellAsync(new DateOnly(2026, 9, 1), 10, euro);
        await noRate.Should().ThrowAsync<ValidationException>("no rate on or before that day, and none typed");

        await SellAsync(new DateOnly(2026, 3, 10), 100);
        await SellAsync(new DateOnly(2026, 5, 10), 200);
        var reports = new ReportService(context, visibility);
        var (from, to) = (new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        var calendar = await reports.TrialBalanceAsync(from, to, null, "en", null, ct);
        calendar.FiscalYearStart.Should().Be(new DateOnly(2026, 1, 1), "January when the company says nothing");
        calendar.Rows.Single(r => r.Code == "705").YearCredit.Should().Be(100 + 200 + 980);

        var fiscal = (await context.Set<CostCenterTypeInfo>().ToListAsync(ct)).Single(p => p.Name.In("en") == ReportService.FiscalYearProperty);
        context.Add(new CostCenterInfo { ElementId = company.Corporate, TypeInfoId = fiscal.Id, Value = JsonValue.Create(4m), TenantId = tenantId, OwnerId = company.Corporate });
        await context.SaveChangesAsync(ct);
        var fromApril = await reports.TrialBalanceAsync(from, to, null, "en", null, ct);
        fromApril.FiscalYearStart.Should().Be(new DateOnly(2026, 4, 1));
        var income705 = fromApril.Rows.Single(r => r.Code == "705");
        (income705.Credit, income705.YearCredit).Should().Be((980m, 200m + 980m), "the period's own, and the fiscal year's from April: March's sale is in the year before");
    }
}

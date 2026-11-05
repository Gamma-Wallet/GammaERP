using System.Globalization;
using AwesomeAssertions;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>The dashboard: its items come from the configuration, a total meets the same stretch before, and Menu access opens it.</summary>
public sealed class DashboardTests : IAsyncLifetime
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

    [Theory]
    [InlineData("2026-11-01", "2026-11-02", "2026-10-01", "2026-10-02")]
    [InlineData("2026-10-01", "2026-10-31", "2026-09-01", "2026-09-30")]
    [InlineData("2026-03-01", "2026-03-31", "2026-02-01", "2026-02-28")]
    [InlineData("2026-01-01", "2026-06-30", "2025-07-01", "2025-12-31")]
    [InlineData("2026-10-10", "2026-10-19", "2026-09-30", "2026-10-09")]
    public void A_total_is_set_against_the_same_stretch_before(string from, string to, string previousFrom, string previousTo) =>
        DashboardService.PreviousPeriod(DateOnly.Parse(from, CultureInfo.InvariantCulture), DateOnly.Parse(to, CultureInfo.InvariantCulture)).Should().Be((DateOnly.Parse(previousFrom, CultureInfo.InvariantCulture), DateOnly.Parse(previousTo, CultureInfo.InvariantCulture)));

    [Fact]
    public async Task The_items_come_from_the_configuration_in_their_order_and_menu_access_opens_it()
    {
        var ct = TestContext.Current.CancellationToken;
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
        await using var context = Context(tenant);
        var owner = company.Corporate;
        var request = new RequestContext(tenantId, null, owner, new HashSet<Guid>(), IsAdministrator: true);
        var visibility = new Visibility(context, request);
        var dashboard = new DashboardService(context, visibility, new StockReportService(context, visibility));

        (await context.Set<DashboardItem>().Where(i => i.OwnerId == owner).CountAsync(ct)).Should().Be(11, "the pack brings the starter items");
        var day = new DateOnly(2026, 10, 15);
        var empty = await dashboard.DashboardAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null, "en", ct);
        empty.Items.Select(i => i.Code).Should().StartWith(["SALES", "PURCHASES", "CLIENTS_OWE"], "the configured order");
        empty.Currency.Should().NotBeNullOrEmpty();
        empty.Items.Single(i => i.Code == "SALES").Should().BeEquivalentTo(new { Value = 0m, Previous = 0m, FirstName = "Sales" });
        var months = empty.Items.Single(i => i.Code == "SALES_PURCHASES");
        months.Rows.Should().HaveCount(12);
        months.Rows[^1].Name.Should().Be("2026-10");
        months.SecondName.Should().Be("Purchases");

        var item = await context.Set<DashboardItem>().SingleAsync(i => i.OwnerId == owner && i.Code == "SALES", ct);
        context.Remove(item);
        await context.SaveChangesAsync(ct);
        (await dashboard.DashboardAsync(day, day, null, "en", ct)).Items.Should().NotContain(i => i.Code == "SALES", "what is shown is what is configured");

        var staff = new DashboardService(context, new Visibility(context, request with { IsAdministrator = false }), new StockReportService(context, visibility));
        var refused = () => staff.DashboardAsync(day, day, null, "en", ct);
        (await refused.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("rights");
    }
}

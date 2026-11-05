using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Gamma.Erp.Api.Auth;
using Gamma.Erp.Api.Setup;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Api.Tests;

/// <summary>Who may do what (H4): a role's rights on a category, where a user works, a user's own password and an administrator's reset.</summary>
public sealed class AccessTests : IAsyncLifetime
{
    private const string Password = "test-only-password-1";
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private WebApplicationFactory<Program> _api = null!;

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        var options = PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(), _db.GetConnectionString())
            .AddInterceptors(new TenantConnectionInterceptor(new TenantContext()));
        await using (var context = new ErpDbContext((DbContextOptions<ErpDbContext>)options.Options))
        {
            await context.Database.MigrateAsync();
        }

        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Development");
            host.UseSetting("ConnectionStrings:Erp", _db.GetConnectionString());
            host.UseSetting("Auth:RedirectUris:0", "http://localhost/signed-in");
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _db.DisposeAsync();
    }

    private Task<HttpClient?> SignInAsync(string username, string password) => _api.SignInAsync(username, password);

    [Fact]
    public async Task A_company_works_in_its_language_and_English_and_whatever_is_saved_has_both_names()
    {
        var ct = TestContext.Current.CancellationToken;
        var made = await _api.Services.CreateCompanyAsync(new SignUpRequest("lang.owner", Password, "AL", "Gjuha Lura", Activity: "BAR", CashDesks: ["Arka"]), ct);
        var company = made.Company;
        Guid clients, ltd;
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var row = await db.Set<CostCenter>().IgnoreQueryFilters().SingleAsync(c => c.Id == company, ct);
            row.Language.Should().Be("sq");
            row.Name.Values.Keys.Should().Contain(["en", "sq"]);
            var accounts = await db.Set<Gamma.Erp.Models.Charts.Chart>().IgnoreQueryFilters().Where(c => c.OwnerId == company).ToListAsync(ct);
            accounts.Should().NotBeEmpty().And.OnlyContain(c => c.Name.Values.ContainsKey("en") && c.Name.Values.ContainsKey("sq"), "a new company's chart has both names");
            accounts.Single(c => c.Code == "605").Name.In("en").Should().Be("Purchases of goods and services");
            clients = (await db.Set<PartnerCategory>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == company && c.Code == "CLIENTS", ct)).Id;
            ltd = (await db.Set<PartnerType>().IgnoreQueryFilters().SingleAsync(t => t.OwnerId == company && t.Code == "LTD", ct)).Id;
        }

        var owner = (await SignInAsync("lang.owner", Password))!;
        var me = await owner.GetFromJsonAsync<JsonElement>("/account/me", ct);
        me.GetProperty("costCenters")[0].GetProperty("language").GetString().Should().Be("sq");
        me.GetProperty("language").ValueKind.Should().Be(JsonValueKind.Null, "nothing chosen yet: English, the default");
        me.GetProperty("costCenters")[0].GetProperty("isGroup").GetBoolean().Should().BeFalse("a single company is no group");
        (await owner.PutAsJsonAsync("/api/v1/account/language", new LanguageChoice("sq"), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetFromJsonAsync<JsonElement>("/account/me", ct)).GetProperty("language").GetString().Should().Be("sq");
        (await owner.PutAsJsonAsync("/api/v1/account/language", new LanguageChoice("not a language"), ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var saved = await owner.PostAsJsonAsync("/api/v1/partners", new ElementDto { Name = new() { ["sq"] = "Hotel Vlora" }, TypeId = ltd, CategoryIds = [clients] }, ct);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(ct));
        var name = (await saved.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("name");
        name.GetProperty("en").GetString().Should().Be("Hotel Vlora", "a name typed once is the other language's too");
        name.GetProperty("sq").GetString().Should().Be("Hotel Vlora");
    }

    [Fact]
    public async Task A_screen_answers_only_whoever_has_it_in_the_menu_and_what_a_document_form_needs_stays_open()
    {
        var ct = TestContext.Current.CancellationToken;
        var made = await _api.Services.CreateCompanyAsync(new SignUpRequest("menu.owner", Password, "AL", "Menu Lura", Activity: "BAR",
            CashDesks: ["Arka"], Users: [new StartUser("menu.staff", Password)]), ct);
        var company = made.Company;
        Guid staffId, users, cash, currency;
        List<Menu> menu;
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var staffUser = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Username == "menu.staff", ct);
            staffId = staffUser.Id;
            users = (await db.Set<UserCategory>().IgnoreQueryFilters().SingleAsync(c => c.Code == "USERS" && c.TenantId == staffUser.TenantId, ct)).Id;
            cash = (await db.Set<CostCenter>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == company && c.Code == "CASH1", ct)).Id;
            currency = await db.Set<Gamma.Erp.Models.Currencies.Currency>().IgnoreQueryFilters().Where(c => c.OwnerId == company).Select(c => c.Id).FirstAsync(ct);
            menu = await db.Set<Menu>().IgnoreQueryFilters().Where(m => m.TenantId == staffUser.TenantId && m.Screen != null).ToListAsync(ct);
        }

        var owner = (await SignInAsync("menu.owner", Password))!;
        (await owner.PutAsJsonAsync($"/api/v1/users/{staffId}/cost-centers", new[] { cash }, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var staff = (await SignInAsync("menu.staff", Password))!;
        staff.DefaultRequestHeaders.Add(RequestContextMiddleware.NodeHeader, cash.ToString());
        owner.DefaultRequestHeaders.Add(RequestContextMiddleware.NodeHeader, cash.ToString());

        const string Year = "from=2026-01-01&to=2026-12-31";
        var someone = Guid.NewGuid();
        (string Screen, string? Parameter, string Route)[] screens =
        [
            ("report", "trial-balance", $"/api/v1/reports/trial-balance?{Year}"), ("report", "trial-balance", $"/api/v1/reports/trial-balance/xlsx?{Year}"),
            ("report", "trial-balance", $"/api/v1/reports/trial-balance/pdf?{Year}"), ("report", "balance-sheet", "/api/v1/reports/balance-sheet?to=2026-12-31"),
            ("report", "lines-by-partner", $"/api/v1/reports/lines?{Year}"), ("report", "transfers", $"/api/v1/reports/lines?{Year}"), ("report", "stock", "/api/v1/reports/stock"),
            ("report", "unbalanced-scenarios", "/api/v1/posting/unbalanced-scenarios"), ("report", "partner-ledger", $"/api/v1/partners/{someone}/ledger?{Year}"),
            ("report", "staff-ledger", $"/api/v1/employees/{someone}/ledger?{Year}"), ("report", "cost-center-ledger", $"/api/v1/cost-centers/{cash}/ledger?{Year}"),
            ("report", "partner-statistics", $"/api/v1/statistics/partners?category={someone}"), ("production", null, $"/api/v1/production/orders?type={someone}"),
            ("stock-convert", null, $"/api/v1/stock/factor?from={someone}&to={someone}"), ("serials", null, "/api/v1/serials/stock"), ("depreciation", null, "/api/v1/depreciation/books"),
            ("asset-value", null, "/api/v1/depreciation/books"), ("currencies", null, "/api/v1/currencies"), ("currencies", null, $"/api/v1/exchange-rates?currency={currency}"),
            ("files", null, "/api/v1/files"), ("pos", null, "/api/v1/pos/barcodes"),
        ];

        foreach (var (screen, parameter, route) in screens)
        {
            (await staff.GetAsync(route, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{route} without {screen} {parameter}");
            (await owner.GetAsync(route, ct)).StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"an administrator opens {route}");
            var row = menu.First(m => m.Screen == screen && (parameter is null || m.Parameter == parameter));
            (await owner.PutAsJsonAsync($"/api/v1/roles/{users}/menu", new[] { row.Id }, ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await staff.GetAsync(route, ct)).StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"{route} with {screen} {parameter}");
            (await owner.PutAsJsonAsync($"/api/v1/roles/{users}/menu", Array.Empty<Guid>(), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var ledger = menu.First(m => m.Parameter == "ledger");
        (await owner.PutAsJsonAsync($"/api/v1/roles/{users}/menu", new[] { ledger.Id }, ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await staff.GetAsync($"/api/v1/reports/trial-balance?{Year}", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "one report does not open another");
        (await owner.PutAsJsonAsync($"/api/v1/roles/{users}/menu", Array.Empty<Guid>(), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await staff.PostAsJsonAsync("/api/v1/exchange-rates", new { currencyId = currency, rateDate = "2026-10-01", rate = 1 }, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await staff.DeleteAsync($"/api/v1/currencies/{currency}", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (var open in (string[])[$"/api/v1/exchange-rates/rate?currency={currency}&date=2026-10-01", "/api/v1/serials", $"/api/v1/cost-centers/{cash}/balance",
            $"/api/v1/partners/{someone}/statistics", $"/api/v1/pos/settings?pointOfSale={cash}", $"/api/v1/pos/places?pointOfSale={cash}"])
        {
            (await staff.GetAsync(open, ct)).StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"a document form or a register's setup reads {open}");
        }
    }

    [Fact]
    public async Task A_role_sees_a_category_once_given_the_right_a_user_works_where_told_and_passwords_change_only_as_hashes()
    {
        var ct = TestContext.Current.CancellationToken;
        var made = await _api.Services.CreateCompanyAsync(new SignUpRequest("bar.owner", Password, "AL", "Bar Lura", Activity: "BAR",
            CashDesks: ["Arka"], Users: [new StartUser("bar.waiter", Password)]), TestContext.Current.CancellationToken);
        var company = made.Company;
        Guid waiter, users, clients, cash, ltd;
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            waiter = (await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Username == "bar.waiter", ct)).Id;
            users = (await db.Set<UserCategory>().IgnoreQueryFilters().SingleAsync(c => c.Code == "USERS" && c.TenantId == db.Set<User>().IgnoreQueryFilters()
                .Where(u => u.Id == waiter).Select(u => u.TenantId).First(), ct)).Id;
            clients = (await db.Set<PartnerCategory>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == company && c.Code == "CLIENTS", ct)).Id;
            ltd = (await db.Set<PartnerType>().IgnoreQueryFilters().SingleAsync(t => t.OwnerId == company && t.Code == "LTD", ct)).Id;
            cash = (await db.Set<CostCenter>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == company && c.Code == "CASH1", ct)).Id;
        }

        var owner = (await SignInAsync("bar.owner", Password))!;
        (await owner.PostAsJsonAsync("/api/v1/partners", new ElementDto { Name = new() { ["sq"] = "Hotel Vlora" }, TypeId = ltd, CategoryIds = [clients] }, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var staff = (await SignInAsync("bar.waiter", Password))!;
        async Task<int> ClientsSeenAsync() => (await staff.GetFromJsonAsync<JsonElement>($"/api/v1/partners?category={clients}", ct)).GetProperty("total").GetInt32();
        (await ClientsSeenAsync()).Should().Be(0, "a user with no rights on the category sees nothing of it");

        var given = await owner.PutAsJsonAsync($"/api/v1/roles/{users}/rights", new[] { new RightDto("tbl_partner", clients, ReadAll: true, ReadOwn: false, Write: false, Forward: false) }, ct);
        given.StatusCode.Should().Be(HttpStatusCode.OK, await given.Content.ReadAsStringAsync(ct));
        (await ClientsSeenAsync()).Should().Be(1, "the role now reads the whole category");
        (await owner.GetFromJsonAsync<RightDto[]>($"/api/v1/roles/{users}/rights", ct)).Should().ContainSingle().Which.ReadAll.Should().BeTrue();
        var badCategory = await owner.PutAsJsonAsync($"/api/v1/roles/{users}/rights", new[] { new RightDto("tbl_partner", cash, true, false, false, false) }, ct);
        badCategory.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a cost centre is not a partner category");
        var notAdmin = await staff.PutAsJsonAsync($"/api/v1/roles/{users}/rights", Array.Empty<RightDto>(), ct);
        notAdmin.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await notAdmin.Content.ReadAsStringAsync(ct)).Should().Contain("NotAllowedError");

        var placed = await owner.PutAsJsonAsync($"/api/v1/users/{waiter}/cost-centers", new[] { cash }, ct);
        placed.StatusCode.Should().Be(HttpStatusCode.OK, await placed.Content.ReadAsStringAsync(ct));
        (await owner.GetFromJsonAsync<Guid[]>($"/api/v1/users/{waiter}/cost-centers", ct)).Should().Equal(cash);

        var wrong = await staff.PostAsJsonAsync("/api/v1/account/password", new PasswordChange("not-the-password", "a-new-password-2"), ct);
        wrong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var tooShort = await staff.PostAsJsonAsync("/api/v1/account/password", new PasswordChange(Password, "short"), ct);
        (await tooShort.Content.ReadAsStringAsync(ct)).Should().Contain("at least 12");
        (await staff.PostAsJsonAsync("/api/v1/account/password", new PasswordChange(Password, "a-new-password-2"), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SignInAsync("bar.waiter", Password)).Should().BeNull("the old password no longer works");
        (await SignInAsync("bar.waiter", "a-new-password-2")).Should().NotBeNull();

        (await owner.PostAsJsonAsync($"/api/v1/users/{waiter}/password", new PasswordReset("reset-by-owner-3"), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SignInAsync("bar.waiter", "reset-by-owner-3")).Should().NotBeNull();
        (await staff.PostAsJsonAsync($"/api/v1/users/{waiter}/password", new PasswordReset("by-the-waiter-4"), ct)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "only an administrator resets a password");
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            (await db.Set<User>().IgnoreQueryFilters().Where(u => u.Username == "bar.waiter").Select(u => u.PasswordHash).ToListAsync(ct))
                .Should().ContainSingle("no history copy of an old password").Which.Should().NotContain("reset-by-owner-3");
        }
    }
}

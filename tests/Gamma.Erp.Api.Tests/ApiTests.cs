using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Gamma.Erp.Api.Auth;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Charts;
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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Api.Tests;

/// <summary>Sign-in (OpenID Connect, code + PKCE), the request's node and a document through the real API host, on the example organisation.</summary>
public sealed class ApiTests : IAsyncLifetime
{
    private const string Password = "test-only-password-1";
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private WebApplicationFactory<Program> _api = null!;
    private OrganisationIds _org = null!;
    private Guid _warehouse;
    private Guid _client;

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        var tenant = new TenantContext();
        var options = PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(), _db.GetConnectionString())
            .AddInterceptors(new TenantConnectionInterceptor(tenant), new HistoryInterceptor(tenant, TimeProvider.System));
        await using (var context = new ErpDbContext((DbContextOptions<ErpDbContext>)options.Options))
        {
            await context.Database.MigrateAsync();
            CompanySetup[] companies = [new("TIRANA", "Fast Food Tirana", "AL", [("cost_center_type:WAREHOUSE", "WH1", "Warehouse")])];
            _org = Organisation.Create(context, Guid.CreateVersion7(), "Gamma Group Inc.", "US", companies);
            Organisation.SetUpCompany(context, _org, companies[0]);
            await context.SaveChangesAsync();
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = _org.TenantId };
            var admins = new UserCategory { Code = "ADMINISTRATORS", Name = TranslatedText.English("Administrators"), IsElement = true, IsAdministrator = true, TenantId = _org.TenantId };
            context.AddRange(staff, admins);
            await context.SaveChangesAsync();
            var admin = new User { TypeId = staff.Id, Code = "ADMIN", Name = TranslatedText.English("Administrator"), Username = "admin", PasswordHash = SignInService.Hash(Password),
                TenantId = _org.TenantId };
            context.Add(admin);
            await context.SaveChangesAsync();
            context.AddRange(new InterUserCategory { ElementId = admin.Id, CategoryId = admins.Id, TenantId = _org.TenantId },
                new InterUserCostCenter { UserId = admin.Id, CostCenterId = _org.Companies["TIRANA"], TenantId = _org.TenantId });
            var client = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "SME")).Id, Code = "C1", Name = TranslatedText.English("Bar Lura"),
                TenantId = _org.TenantId, OwnerId = _org.Companies["TIRANA"] };
            context.Add(client);
            await context.SaveChangesAsync();
            (_client, _warehouse) = (client.Id, (await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH1")).Id);
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

    /// <summary>The OpenID Connect flow a browser runs: authorize, the sign-in page, the code, the token.</summary>
    private async Task<(HttpStatusCode SignIn, string? Token)> SignInAsync(string password)
    {
        var browser = _api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var authorize = "/connect/authorize?client_id=gamma-erp-web&response_type=code&scope=openid%20offline_access"
            + $"&redirect_uri={Uri.EscapeDataString("http://localhost/signed-in")}&code_challenge={challenge}&code_challenge_method=S256";
        (await browser.GetAsync(authorize)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        var login = await browser.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "admin", ["password"] = password, ["returnUrl"] = authorize,
        }));
        if (login.Headers.Location?.ToString().Contains("error=", StringComparison.Ordinal) == true)
        {
            return (login.StatusCode, null);
        }

        var code = System.Web.HttpUtility.ParseQueryString((await browser.GetAsync(authorize)).Headers.Location!.Query)["code"];
        var token = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "gamma-erp-web", ["code"] = code!, ["redirect_uri"] = "http://localhost/signed-in", ["code_verifier"] = verifier,
        }));
        token.EnsureSuccessStatusCode();
        return (login.StatusCode, JsonDocument.Parse(await token.Content.ReadAsStringAsync()).RootElement.GetProperty("access_token").GetString());
    }

    private HttpClient Api(string token)
    {
        var api = _api.CreateClient();
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return api;
    }

    [Fact]
    public async Task Signing_in_refuses_a_wrong_password_and_issues_a_token_for_the_right_one_recorded_and_hashed()
    {
        var (_, none) = await SignInAsync("wrong");
        none.Should().BeNull();
        var (_, token) = await SignInAsync(Password);
        token.Should().NotBeNullOrEmpty();

        var ct = TestContext.Current.CancellationToken;
        (await _api.CreateClient().GetAsync("/api/v1/partners/categories", ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "no token");
        var categories = await Api(token!).GetFromJsonAsync<JsonElement>("/api/v1/partners/categories", ct);
        categories.EnumerateArray().Select(c => c.GetProperty("code").GetString()).Should().Contain(["CLIENTS", "SUPPLIERS"]);

        // A place below the user's own is theirs to work at (the founder, 2026-10-31); one outside their tree is not.
        var below = Api(token!);
        below.DefaultRequestHeaders.Add("X-Gamma-Node", _warehouse.ToString());
        (await below.GetAsync("/api/v1/partners/categories", ct)).StatusCode.Should().Be(HttpStatusCode.OK, "the warehouse is below the user's company");
        var odd = Api(token!);
        odd.DefaultRequestHeaders.Add("X-Gamma-Node", Guid.NewGuid().ToString());
        (await odd.GetAsync("/api/v1/partners/categories", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "it is not in the user's tree");

        await using var scope = _api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Username == "admin" && u.Active, ct);
        user.PasswordHash.Should().NotContain(Password, "only the hash is kept");
        (await db.Set<Login>().IgnoreQueryFilters().CountAsync(l => l.UserId == user.Id && l.InOut, ct)).Should().Be(1, "one good sign-in, recorded");
    }

    [Fact]
    public async Task A_document_is_saved_and_posted_through_the_API_and_a_refusal_comes_back_as_422_with_its_code()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, token) = await SignInAsync(Password);
        var api = Api(token!);
        Guid type, category, gl, vat, revenue, revenueType;
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var sales = await db.Set<DocumentCategory>().IgnoreQueryFilters().SingleAsync(c => c.Code == "SALES" && c.Active, ct);
            var inSales = db.Set<InterDocumentTypeCategory>().IgnoreQueryFilters().Where(l => l.CategoryId == sales.Id && l.Active).Select(l => l.TypeId);
            type = (await db.Set<DocumentType>().IgnoreQueryFilters().SingleAsync(t => t.Code == "FSHM" && t.Active && inSales.Contains(t.Id), ct)).Id;
            category = sales.Id;
            gl = (await db.Set<TransactionType>().IgnoreQueryFilters().SingleAsync(t => t.Code == "ACCOUNT" && t.Active, ct)).Id;
            vat = (await db.Set<VatGroup>().IgnoreQueryFilters().SingleAsync(v => v.Rate == 20 && v.Active, ct)).Id;
            // A sale's line takes an income account: sales of goods (705) of the standard chart.
            var goods = await db.Set<Chart>().IgnoreQueryFilters().SingleAsync(c => c.Code == "705" && c.Active, ct);
            (revenue, revenueType) = (goods.Id, goods.TypeId);
        }

        var invoice = new DocumentDto
        {
            TypeId = type, CategoryIds = [category], PostingDate = new DateOnly(2026, 10, 29), FromId = _warehouse, FromName = "Warehouse", ToId = _client, ToName = "Bar Lura",
            Transactions = [new TransactionDto { TypeId = gl, ElementId = revenue, ElementTypeId = revenueType, ElementName = "Sales", Price = 100, VatGroupId = vat }],
        };
        var saved = await (await api.PostAsJsonAsync("/api/v1/documents", invoice, ct)).Content.ReadFromJsonAsync<DocumentDto>(ct);
        var posted = await (await api.PostAsync($"/api/v1/documents/{saved!.Id}/post", null, ct)).Content.ReadFromJsonAsync<DocumentDto>(ct);

        posted!.Posted.Should().BeTrue();
        posted.Totals.Should().ContainSingle().Which.ValueVat.Should().Be(120);
        var again = await api.PostAsync($"/api/v1/documents/{saved.Id}/post", null, ct);
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await again.Content.ReadAsStringAsync(ct)).Should().Contain("PostedError");
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Gamma.Erp.Api.Setup;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Posting;
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

/// <summary>Sign-up and the guided start through the real API host: a single company, a group, a company copied from another, the refusals.</summary>
public sealed class SetupTests : IAsyncLifetime
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

    private async Task<string> SignInAsync(string username)
    {
        var browser = _api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var authorize = "/connect/authorize?client_id=gamma-erp-web&response_type=code&scope=openid%20offline_access"
            + $"&redirect_uri={Uri.EscapeDataString("http://localhost/signed-in")}&code_challenge={challenge}&code_challenge_method=S256";
        await browser.GetAsync(authorize);
        var login = await browser.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username, ["password"] = Password, ["returnUrl"] = authorize,
        }));
        login.Headers.Location!.ToString().Should().NotContain("error=");
        var code = System.Web.HttpUtility.ParseQueryString((await browser.GetAsync(authorize)).Headers.Location!.Query)["code"];
        var token = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "gamma-erp-web", ["code"] = code!, ["redirect_uri"] = "http://localhost/signed-in", ["code_verifier"] = verifier,
        }));
        token.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await token.Content.ReadAsStringAsync()).RootElement.GetProperty("access_token").GetString()!;
    }

    private HttpClient Api(string token)
    {
        var api = _api.CreateClient();
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return api;
    }

    private async Task<SignUpResult> SignUpAsync(SignUpRequest request)
    {
        return await _api.Services.CreateCompanyAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<bool> InScopeUsernameFreeAsync(string username, CancellationToken ct)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SetupService>().UsernameFreeAsync(username, ct);
    }

    private async Task<T> InDbAsync<T>(Func<ErpDbContext, Task<T>> read)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<ErpDbContext>());
    }

    [Fact]
    public async Task There_is_no_way_in_from_outside_and_the_sign_in_page_starts_with_a_user_name_given_or_hinted()
    {
        var ct = TestContext.Current.CancellationToken;
        // An ERP has no public side (the founder, 2026-11-02): no sign-up link, no sign-up, and nothing of the setup answers without signing in.
        var page = await _api.CreateClient().GetStringAsync("/account/login", ct);
        page.Should().NotContain("sign-up");
        (await _api.CreateClient().PostAsJsonAsync("/api/v1/setup/sign-up", new SignUpRequest("outside", Password, "AL", "Outside"), ct)).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed, HttpStatusCode.Unauthorized);
        (await _api.CreateClient().GetAsync("/api/v1/setup/countries", ct)).StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Redirect);
        (await _api.CreateClient().GetAsync("/api/v1/setup/username-free?username=x", ct)).StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await _api.CreateClient().GetStringAsync("/account/login?username=new.owner", ct)).Should().Contain("value=\"new.owner\"");
        var authorize = Uri.EscapeDataString("/connect/authorize?client_id=gamma-erp-web&login_hint=new.owner");
        (await _api.CreateClient().GetStringAsync($"/account/login?returnUrl={authorize}", ct)).Should().Contain("value=\"new.owner\"");
        (await _api.CreateClient().GetStringAsync("/account/login?username=%22%3E%3Cscript%3E", ct)).Should().NotContain("<script>", "the name is encoded");
    }

    [Fact]
    public async Task A_single_company_signs_up_with_its_pack_profile_cash_desk_and_users_and_its_owner_signs_in()
    {
        var ct = TestContext.Current.CancellationToken;
        var countries = SetupService.Countries();
        countries.Select(c => c.Code).Should().Contain(["AL", "IT", "CY", "LT"]);
        countries.Single(c => c.Code == "LT").Currency.Should().Be("EUR");

        var made = await SignUpAsync(new SignUpRequest("Kirpykla.Owner", Password, "LT", "Kirpykla Vilnius", Activity: "BARBER", CashDesks: ["Kasa"],
            Banks: ["Swedbank"], Users: [new StartUser("kirpykla.staff", Password)]));
        made.Root.Should().Be(made.Company, "a single company is its own root");
        made.Rows.Should().BeGreaterThan(100);

        (await InScopeUsernameFreeAsync("KIRPYKLA.OWNER", ct))
            .Should().BeFalse("names are compared trimmed and in lower case");
        var (documentTypes, centres, owner) = await InDbAsync(async db => (
            await db.Set<DocumentType>().IgnoreQueryFilters().CountAsync(t => t.OwnerId == made.Company && t.Active, ct),
            await db.Set<CostCenter>().IgnoreQueryFilters().Where(c => c.IdSup == made.Company).Select(c => c.Code).ToListAsync(ct),
            await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == made.Owner, ct)));
        documentTypes.Should().Be(63, "the Barber activity takes the Beauty profile, not the pack's 91");
        centres.Should().BeEquivalentTo(["CASH1", "BANK1"]);
        owner.Username.Should().Be("kirpykla.owner");
        owner.PasswordHash.Should().NotContain(Password);

        var categories = await Api(await SignInAsync("kirpykla.owner")).GetFromJsonAsync<JsonElement>("/api/v1/partners/categories", ct);
        categories.GetArrayLength().Should().BeGreaterThan(0, "the owner works at the company");
    }

    [Fact]
    public async Task A_signed_up_restaurant_posts_its_first_sale_and_cash_receipt_through_the_API_on_the_accounts_its_pack_proposes()
    {
        var ct = TestContext.Current.CancellationToken;
        var made = await SignUpAsync(new SignUpRequest("durres.owner", Password, "AL", "Restorant Durrësi", Activity: "RESTAURANT", CashDesks: ["Arka"], Banks: ["BKT"]));
        var api = Api(await SignInAsync("durres.owner"));

        // The cash desk and the bank account hold the accounts the Albanian pack proposes (5311 cash, 5121 bank): nothing else to set up.
        var (held, ids) = await InDbAsync(async db =>
        {
            var accounts = await db.Set<Chart>().IgnoreQueryFilters().Where(c => c.OwnerId == made.Company).ToDictionaryAsync(c => c.Code!, c => c.Id, ct);
            var centres = await db.Set<CostCenter>().IgnoreQueryFilters().Where(c => c.OwnerId == made.Company && (c.Code == "CASH1" || c.Code == "BANK1"))
                .ToDictionaryAsync(c => c.Code!, c => c.Id, ct);
            var values = await db.Set<CostCenterInfo>().IgnoreQueryFilters().Where(i => centres.Values.Contains(i.ElementId)).ToListAsync(ct);
            var byCentre = centres.ToDictionary(c => c.Key, c => values.Where(v => v.ElementId == c.Value).Select(v => v.Value!.GetValue<string>()).ToList());
            var sales = await db.Set<DocumentCategory>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == made.Company && c.Code == "SALES", ct);
            var cashDesk = await db.Set<DocumentCategory>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == made.Company && c.Code == "CASH_DESK", ct);
            async Task<Guid> TypeIn(Guid category, string code) => (await db.Set<DocumentType>().IgnoreQueryFilters().SingleAsync(t => t.OwnerId == made.Company && t.Code == code
                && db.Set<InterDocumentTypeCategory>().IgnoreQueryFilters().Any(l => l.TypeId == t.Id && l.CategoryId == category), ct)).Id;
            return (byCentre, new
            {
                Accounts = accounts, Cash = centres["CASH1"], Sales = sales.Id, CashDesk = cashDesk.Id, Sale = await TypeIn(sales.Id, "FSHM"), Receipt = await TypeIn(cashDesk.Id, "MA"),
                Line = (await db.Set<TransactionType>().IgnoreQueryFilters().SingleAsync(t => t.OwnerId == made.Company && t.Code == "ACCOUNT", ct)).Id,
                Ltd = (await db.Set<PartnerType>().IgnoreQueryFilters().SingleAsync(t => t.OwnerId == made.Company && t.Code == "LTD", ct)).Id,
                Clients = (await db.Set<PartnerCategory>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == made.Company && c.Code == "CLIENTS", ct)).Id,
                Vat20 = (await db.Set<VatGroup>().IgnoreQueryFilters().SingleAsync(v => v.OwnerId == made.Company && v.Rate == 20, ct)).Id,
                Vat0 = (await db.Set<VatGroup>().IgnoreQueryFilters().SingleAsync(v => v.OwnerId == made.Company && v.Rate == 0, ct)).Id,
                ChartTypes = await db.Set<Chart>().IgnoreQueryFilters().Where(c => c.OwnerId == made.Company).ToDictionaryAsync(c => c.Id, c => c.TypeId, ct),
            });
        });
        held["CASH1"].Should().Contain(ids.Accounts["5311"].ToString(), "its currency is proposed too");
        held["BANK1"].Should().Contain(ids.Accounts["5121"].ToString());
        var cashDesk = await api.GetFromJsonAsync<ElementDto>($"/api/v1/cost-centers/{ids.Cash}", ct);
        cashDesk!.CategoryIds.Should().ContainSingle("the owner sees the cash desk: it is in its type's category");

        var partner = await api.PostAsJsonAsync("/api/v1/partners", new ElementDto { Name = new() { ["sq"] = "Hotel Adriatik" }, TypeId = ids.Ltd, CategoryIds = [ids.Clients] }, ct);
        partner.StatusCode.Should().Be(HttpStatusCode.OK, await partner.Content.ReadAsStringAsync(ct));
        var client = (await partner.Content.ReadFromJsonAsync<ElementDto>(ct))!.Id!.Value;

        async Task<DocumentDto> PostAsync(Guid type, Guid category, (Guid Id, string Name) from, (Guid Id, string Name) to, string account, decimal price, Guid vat)
        {
            var saved = await api.PostAsJsonAsync("/api/v1/documents", new DocumentDto
            {
                TypeId = type, CategoryIds = [category], DocumentDate = new DateOnly(2026, 10, 30), PostingDate = new DateOnly(2026, 10, 30),
                FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name,
                Transactions = [new TransactionDto { TypeId = ids.Line, ElementId = ids.Accounts[account], ElementTypeId = ids.ChartTypes[ids.Accounts[account]], ElementName = account, Price = price,
                    VatGroupId = vat }],
            }, ct);
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(ct));
            var posted = await api.PostAsync($"/api/v1/documents/{(await saved.Content.ReadFromJsonAsync<DocumentDto>(ct))!.Id}/post", null, ct);
            posted.StatusCode.Should().Be(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync(ct));
            return (await posted.Content.ReadFromJsonAsync<DocumentDto>(ct))!;
        }

        var sale = await PostAsync(ids.Sale, ids.Sales, (ids.Cash, "Arka"), (client, "Hotel Adriatik"), "705", 1000, ids.Vat20);
        var receipt = await PostAsync(ids.Receipt, ids.CashDesk, (client, "Hotel Adriatik"), (ids.Cash, "Arka"), "411", 1200, ids.Vat0);

        var entries = await InDbAsync(db => db.Set<ChartValue>().IgnoreQueryFilters().Where(v => v.DocumentId == sale.Id || v.DocumentId == receipt.Id).ToListAsync(ct));
        var code = ids.Accounts.ToDictionary(a => a.Value, a => a.Key);
        entries.GroupBy(e => code[e.ChartId!.Value]).ToDictionary(g => g.Key, g => g.Sum(e => e.Debit) - g.Sum(e => e.Credit)).Should().BeEquivalentTo(new Dictionary<string, decimal>
        {
            ["705"] = -1000, ["4457"] = -200, ["411"] = 0, ["5311"] = 1200,
        }, "the sale's income and VAT, the client paid in full, the cash in the cash desk's account");
        foreach (var document in entries.GroupBy(e => e.DocumentId))
        {
            document.Sum(e => e.Debit).Should().Be(document.Sum(e => e.Credit), "every document balances");
        }
    }

    [Fact]
    public async Task A_sign_up_is_refused_whole_for_a_taken_name_a_short_password_a_country_without_pack_or_a_group_missing_its_needs()
    {
        var ct = TestContext.Current.CancellationToken;
        await SignUpAsync(new SignUpRequest("first.owner", Password, "AL", "Berberia Arti"));
        async Task<string> Refused(SignUpRequest request)
        {
            var refused = () => _api.Services.CreateCompanyAsync(request, ct);
            return string.Join(' ', (await refused.Should().ThrowAsync<Gamma.Erp.Services.ValidationException>()).Which.Errors.Values);
        }

        (await Refused(new SignUpRequest(" First.Owner ", Password, "AL", "Another"))).Should().Contain("SetupError").And.Contain("taken");
        (await Refused(new SignUpRequest("second.owner", "short", "AL", "Another"))).Should().Contain("at least 12");
        (await Refused(new SignUpRequest("second.owner", Password, "US", "Another"))).Should().Contain("no starter pack");
        (await Refused(new SignUpRequest("second.owner", Password, "AL", "Another", Groups: ["posting"]))).Should().Contain("needs the");
        (await Refused(new SignUpRequest("second.owner", Password, "AL", "Another", Users: [new StartUser("second.owner", Password)]))).Should().Contain("twice");
        (await InDbAsync(db => db.Set<CostCenter>().IgnoreQueryFilters().CountAsync(c => c.Code == "ANOTHER", ct))).Should().Be(0, "nothing of a refused sign-up stays");
    }

    [Fact]
    public async Task The_server_command_makes_a_company_and_its_administrator_and_keeps_the_password_out_of_its_arguments()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _api.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<SetupService>();
        var output = new StringWriter();
        string[] args = ["create-company", "--username", "server.owner", "--country", "al", "--company", "Kompani Serveri"];
        (await CreateCompanyCommand.RunAsync(setup, args, Password, output, ct)).Should().Be(0, output.ToString());
        (await InDbAsync(db => db.Set<Gamma.Erp.Models.Users.User>().IgnoreQueryFilters().CountAsync(u => u.Username == "server.owner", ct))).Should().Be(1);
        (await CreateCompanyCommand.RunAsync(setup, args, null, output, ct)).Should().Be(2, "no password, no company");
        (await CreateCompanyCommand.RunAsync(setup, args, "short", output, ct)).Should().Be(1, "refused, and it says why");
        output.ToString().Should().Contain("Usage").And.Contain("at least 12").And.NotContain(Password);
    }

    [Fact]
    public async Task A_group_adds_a_company_from_a_pack_and_one_copied_from_another_company_with_only_the_groups_chosen()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = await SignUpAsync(new SignUpRequest("gruppo.owner", Password, "IT", "Ristorante Roma", CorporateName: "Gruppo Roma S.p.A.", Activity: "RESTAURANT"));
        group.Root.Should().NotBe(group.Company, "a group has its corporate above the company");
        var owner = Api(await SignInAsync("gruppo.owner"));
        var places = (await owner.GetFromJsonAsync<JsonElement>("/account/me", ct)).GetProperty("costCenters").EnumerateArray().ToList();
        places.Should().ContainSingle(p => p.GetProperty("isGroup").GetBoolean(), "the screens offer a new company at the group, which keeps books of its own too")
            .Which.GetProperty("id").GetGuid().Should().Be(group.Root);

        var fromPack = await owner.PostAsJsonAsync("/api/v1/companies", new CompanyRequest("Supermarket Limassol", "CY", Activity: "SUPERMARKET", CashDesks: ["Ταμείο"]), ct);
        fromPack.StatusCode.Should().Be(HttpStatusCode.Created, await fromPack.Content.ReadAsStringAsync(ct));
        var limassol = (await fromPack.Content.ReadFromJsonAsync<CompanyResult>(ct))!;

        var copied = await owner.PostAsJsonAsync("/api/v1/companies", new CompanyRequest("Ristorante Milano", "IT", CopyFrom: group.Company, Groups: ["chart", "documents"],
            CashDesks: ["Cassa"]), ct);
        copied.StatusCode.Should().Be(HttpStatusCode.Created, await copied.Content.ReadAsStringAsync(ct));
        var milano = (await copied.Content.ReadFromJsonAsync<CompanyResult>(ct))!;

        var counts = await InDbAsync(async db =>
        {
            async Task<(int Accounts, int Documents, int Scenarios, string? Language)> Of(Guid company) => (
                await db.Set<Chart>().IgnoreQueryFilters().CountAsync(c => c.OwnerId == company && c.Active, ct),
                await db.Set<DocumentType>().IgnoreQueryFilters().CountAsync(c => c.OwnerId == company && c.Active, ct),
                await db.Set<Scenario>().IgnoreQueryFilters().CountAsync(c => c.OwnerId == company && c.Active, ct),
                (await db.Set<Chart>().IgnoreQueryFilters().FirstAsync(c => c.OwnerId == company, ct)).Name.Values.Keys.First());
            return (Roma: await Of(group.Company), Limassol: await Of(limassol.Company), Milano: await Of(milano.Company));
        });
        counts.Limassol.Language.Should().Be("el", "Cyprus's pack, in Greek");
        counts.Milano.Accounts.Should().Be(counts.Roma.Accounts, "the chart is copied");
        counts.Milano.Documents.Should().Be(counts.Roma.Documents, "the document types are copied");
        counts.Milano.Scenarios.Should().Be(0, "posting was not chosen");

        // The copies are the new company's own: no link of a copy points at a row of Roma.
        var (romaIds, links) = await InDbAsync(async db => (
            (await db.Set<DocumentType>().IgnoreQueryFilters().Where(t => t.OwnerId == group.Company).Select(t => t.Id).ToListAsync(ct)).ToHashSet(),
            await db.Set<InterDocumentTypeCategory>().IgnoreQueryFilters().Where(l => l.OwnerId == milano.Company).Select(l => l.TypeId).ToListAsync(ct)));
        links.Should().NotBeEmpty().And.NotContain(id => romaIds.Contains(id));

        var refused = await owner.PostAsJsonAsync("/api/v1/companies", new CompanyRequest("Ristorante Milano", "IT"), ct);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "the group already has a company RISTORANTEMI");
    }
}

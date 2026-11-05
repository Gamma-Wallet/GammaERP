using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Gamma.Erp.Api.Auth;
using Gamma.Erp.Api.Setup;
using Gamma.Erp.Models.Base;
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

/// <summary>What the registers screen (C3 phase 3) needs beyond the entity API: lookup choices, the user's own rights, a new user's user name.</summary>
public sealed class RegisterApiTests : IAsyncLifetime
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

    private async Task<HttpClient?> SignInAsync(string username, string password)
    {
        var browser = _api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var authorize = "/connect/authorize?client_id=gamma-erp-web&response_type=code&scope=openid%20offline_access"
            + $"&redirect_uri={Uri.EscapeDataString("http://localhost/signed-in")}&code_challenge={challenge}&code_challenge_method=S256";
        await browser.GetAsync(authorize);
        var login = await browser.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username, ["password"] = password, ["returnUrl"] = authorize,
        }));
        if (login.Headers.Location?.ToString().Contains("error=", StringComparison.Ordinal) != false)
        {
            return null;
        }

        var code = System.Web.HttpUtility.ParseQueryString((await browser.GetAsync(authorize)).Headers.Location!.Query)["code"];
        var token = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "gamma-erp-web", ["code"] = code!, ["redirect_uri"] = "http://localhost/signed-in", ["code_verifier"] = verifier,
        }));
        token.EnsureSuccessStatusCode();
        var api = _api.CreateClient();
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            JsonDocument.Parse(await token.Content.ReadAsStringAsync()).RootElement.GetProperty("access_token").GetString());
        return api;
    }

    [Fact]
    public async Task Lookups_follow_the_entity_rights_a_user_reads_their_own_rights_and_a_new_user_gets_a_user_name_to_sign_in_with()
    {
        var ct = TestContext.Current.CancellationToken;
        var made = await _api.Services.CreateCompanyAsync(new SignUpRequest("bar.owner", Password, "AL", "Bar Lura", Activity: "BAR",
            CashDesks: ["Arka"], Users: [new StartUser("bar.waiter", Password)]), TestContext.Current.CancellationToken);
        var company = made.Company;
        Guid roleUsers, clients, ltd, staffType;
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var tenant = (await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Username == "bar.waiter", ct)).TenantId;
            roleUsers = (await db.Set<UserCategory>().IgnoreQueryFilters().SingleAsync(c => c.Code == "USERS" && c.TenantId == tenant, ct)).Id;
            staffType = (await db.Set<UserType>().IgnoreQueryFilters().SingleAsync(t => t.Code == "STAFF" && t.TenantId == tenant, ct)).Id;
            clients = (await db.Set<PartnerCategory>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == company && c.Code == "CLIENTS", ct)).Id;
            ltd = (await db.Set<PartnerType>().IgnoreQueryFilters().SingleAsync(t => t.OwnerId == company && t.Code == "LTD", ct)).Id;
        }

        var owner = (await SignInAsync("bar.owner", Password))!;
        var staff = (await SignInAsync("bar.waiter", Password))!;

        // B2: the signed-in user's own rights.
        (await owner.GetFromJsonAsync<MyRightsDto>("/api/v1/account/rights", ct))!.IsAdministrator.Should().BeTrue();
        (await staff.GetFromJsonAsync<MyRightsDto>("/api/v1/account/rights", ct))!.Rights.Should().BeEmpty();
        (await owner.PutAsJsonAsync($"/api/v1/roles/{roleUsers}/rights", new[] { new RightDto("tbl_partner", clients, ReadAll: true, ReadOwn: false, Write: false, Forward: false) }, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var mine = (await staff.GetFromJsonAsync<MyRightsDto>("/api/v1/account/rights", ct))!;
        mine.IsAdministrator.Should().BeFalse();
        mine.Rights.Should().ContainSingle().Which.Should().Be(new RightDto("tbl_partner", clients, true, false, false, false), "the role's right counts as the user's");

        // B1: lookup scopes and their choices, by the entity's rights.
        (await owner.PostAsJsonAsync("/api/v1/partners", new ElementDto { Code = "C-1", Name = new() { ["sq"] = "Hotel Vlora" }, TypeId = ltd, CategoryIds = [clients] }, ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var scopes = (await owner.GetFromJsonAsync<LookupScopeDto[]>("/api/v1/lookups", ct))!;
        var partners = scopes.Should().Contain(s => s.Table == "tbl_partner" && s.CategoryId == null && s.TypeId == null).Which.Id;
        var currencies = scopes.Should().Contain(s => s.Table == "tbl_currency").Which.Id;

        var found = (await owner.GetFromJsonAsync<PageDto<LookupOptionDto>>($"/api/v1/lookups/{partners}?search=vlora", ct))!;
        found.Items.Should().ContainSingle().Which.Should().Be(new LookupOptionDto(found.Items[0].Id, "C-1", "Hotel Vlora"), "no English name: another language");
        (await owner.GetFromJsonAsync<PageDto<LookupOptionDto>>($"/api/v1/lookups/{partners}?search=nothing-like-this", ct))!.Total.Should().Be(0);
        (await staff.GetFromJsonAsync<PageDto<LookupOptionDto>>($"/api/v1/lookups/{partners}?search=vlora", ct))!.Total.Should().Be(1, "the role reads the clients");
        (await owner.PutAsJsonAsync($"/api/v1/roles/{roleUsers}/rights", Array.Empty<RightDto>(), ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staff.GetFromJsonAsync<PageDto<LookupOptionDto>>($"/api/v1/lookups/{partners}?search=vlora", ct))!.Total.Should().Be(0, "no right, no choices");
        (await owner.GetFromJsonAsync<PageDto<LookupOptionDto>>($"/api/v1/lookups/{currencies}?search=all", ct))!.Items.Should().Contain(o => o.Code == "ALL");
        (await owner.GetAsync($"/api/v1/lookups/{Guid.NewGuid()}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // B3: a user made in the register gets a user name, then a password, and signs in.
        var saved = await owner.PostAsJsonAsync("/api/v1/users", new ElementDto { Code = "CASHIER", Name = new() { ["en"] = "Cashier" }, TypeId = staffType, CategoryIds = [roleUsers] }, ct);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(ct));
        var cashier = (await saved.Content.ReadFromJsonAsync<ElementDto>(ct))!.Id!.Value;
        (await owner.PutAsJsonAsync($"/api/v1/users/{cashier}/username", new UsernameChange(" Bar.Cashier "), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.PostAsJsonAsync($"/api/v1/users/{cashier}/password", new PasswordReset("cashier-password-5"), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SignInAsync("bar.cashier", "cashier-password-5")).Should().NotBeNull("the name is kept in lower case and trimmed");

        var taken = await owner.PutAsJsonAsync($"/api/v1/users/{cashier}/username", new UsernameChange("bar.waiter"), ct);
        taken.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await taken.Content.ReadAsStringAsync(ct)).Should().Contain("taken");
        (await owner.PutAsJsonAsync($"/api/v1/users/{cashier}/username", new UsernameChange("x"), ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var notAdmin = await staff.PutAsJsonAsync($"/api/v1/users/{cashier}/username", new UsernameChange("by.the.waiter"), ct);
        (await notAdmin.Content.ReadAsStringAsync(ct)).Should().Contain("NotAllowedError");
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            (await db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == cashier && u.Active).Select(u => u.Username).SingleAsync(ct)).Should().Be("bar.cashier");
            (await db.Set<Elements>().IgnoreQueryFilters().AnyAsync(e => e.Table == "tbl_currency", ct)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task On_a_shared_type_each_company_sees_saves_and_adds_only_its_own_properties()
    {
        var ct = TestContext.Current.CancellationToken;
        var made = await _api.Services.CreateCompanyAsync(new SignUpRequest("gruppo.owner", Password, "IT", "Ristorante Roma",
            CorporateName: "Gruppo Roma S.p.A.", Activity: "RESTAURANT"), TestContext.Current.CancellationToken);
        var roma = made.Company;
        var owner = (await SignInAsync("gruppo.owner", Password))!;
        var added = await owner.PostAsJsonAsync("/api/v1/companies", new CompanyRequest("Supermarket Limassol", "CY", Activity: "SUPERMARKET", CashDesks: ["Ταμείο"]), ct);
        added.StatusCode.Should().Be(HttpStatusCode.Created, await added.Content.ReadAsStringAsync(ct));
        var limassol = (await added.Content.ReadFromJsonAsync<CompanyResult>(ct))!.Company;

        // The owner works at both companies (an administrator links them), so each request names one.
        var me = (await owner.GetFromJsonAsync<JsonElement>("/account/me", ct)).GetProperty("id").GetGuid();
        (await owner.PutAsJsonAsync($"/api/v1/users/{me}/cost-centers", new[] { roma, limassol }, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        HttpClient At(Guid company)
        {
            var client = _api.CreateClient();
            client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
            client.DefaultRequestHeaders.Add("X-Gamma-Node", company.ToString());
            return client;
        }

        async Task<ElementTypeDto> CompanyTypeAsync(HttpClient client)
        {
            var types = await client.GetAsync("/api/v1/cost-centers/types", ct);
            types.StatusCode.Should().Be(HttpStatusCode.OK, await types.Content.ReadAsStringAsync(ct));
            return (await types.Content.ReadFromJsonAsync<ElementTypeDto[]>(ct))!.Single(t => t.Code == "COMPANY");
        }

        async Task<Dictionary<Guid, (Guid? Owner, bool Active)>> RowsAsync(Guid type)
        {
            await using var scope = _api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            return await db.Set<CostCenterTypeInfo>().IgnoreQueryFilters().Where(p => p.TypeId == type)
                .ToDictionaryAsync(p => p.Id, p => (p.OwnerId, p.Active), ct);
        }

        var atRoma = At(roma);
        var atLimassol = At(limassol);
        var romaType = await CompanyTypeAsync(atRoma);
        var limassolType = await CompanyTypeAsync(atLimassol);
        romaType.Id.Should().Be(limassolType.Id!.Value, "one COMPANY type shared by the group's companies");
        var before = await RowsAsync(romaType.Id!.Value);
        var limassolOwn = before.Where(r => r.Value.Owner == limassol && r.Value.Active).Select(r => r.Key).ToList();
        limassolOwn.Should().NotBeEmpty("Cyprus's pack gave Limassol its own settings");

        // F1: each company sees the shared properties and its own, never the other's.
        romaType.Properties.Select(p => p.Id!.Value).Should().NotContain(limassolOwn);
        limassolType.Properties.Select(p => p.Id!.Value).Should().Contain(limassolOwn);
        romaType.Properties.Select(p => p.Name["en"]).Should().OnlyHaveUniqueItems("a company sees its pack's settings once");

        // F2 + F3: Roma saves the type without one of its properties and with a new one; Limassol's rows are untouched.
        var dropped = romaType.Properties.First(p => p.Name["en"] == "Website");
        var saved = await atRoma.PostAsJsonAsync("/api/v1/cost-centers/types", romaType with
        {
            Properties = [.. romaType.Properties.Where(p => p.Id != dropped.Id), new TypeInfoDto { Name = new() { ["en"] = "Roma only" }, Kind = PropertyKind.Text }],
        }, ct);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(ct));
        var after = await RowsAsync(romaType.Id!.Value);
        limassolOwn.Should().OnlyContain(id => after[id].Active, "another company's settings are never switched off");
        after[dropped.Id!.Value].Active.Should().BeFalse();
        (await CompanyTypeAsync(atRoma)).Properties.Should().ContainSingle(p => p.Name["en"] == "Roma only").Which.Id.Should().NotBeNull();
        var romaOnly = (await CompanyTypeAsync(atRoma)).Properties.Single(p => p.Name["en"] == "Roma only").Id!.Value;
        after[romaOnly].Owner.Should().Be(roma, "a property added to a shared type belongs to the company that added it");
        (await CompanyTypeAsync(atLimassol)).Properties.Should().NotContain(p => p.Name["en"] == "Roma only").And.Contain(p => p.Name["en"] == "Website");

        // A property of the other company cannot be sent at all.
        var foreign = await atRoma.PostAsJsonAsync("/api/v1/cost-centers/types", romaType with
        {
            // Under a name of its own, so the refusal is for whose it is, not for a repeated name.
            Properties = [.. romaType.Properties, limassolType.Properties.First(p => limassolOwn.Contains(p.Id!.Value)) with { Name = new() { ["en"] = "Limassol's" } }],
        }, ct);
        foreign.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await foreign.Content.ReadAsStringAsync(ct)).Should().Contain("does not exist");

        // Each company changes its own "Weight" category though the other company has one of the same name.
        var weight = (await atRoma.GetFromJsonAsync<CategoryDto[]>("/api/v1/units/categories", ct))!.Single(c => c.Code == "WEIGHT");
        var usable = await atRoma.PostAsJsonAsync("/api/v1/units/categories", weight with { IsElement = true }, ct);
        usable.StatusCode.Should().Be(HttpStatusCode.OK, await usable.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task A_place_sees_and_changes_the_cost_centres_below_it_whoever_made_them_and_one_may_work_at_any_place_below_ones_own()
    {
        var ct = TestContext.Current.CancellationToken;
        var made = await _api.Services.CreateCompanyAsync(new SignUpRequest("bar.owner", Password, "AL", "Bar Lura"), TestContext.Current.CancellationToken);
        var company = made.Company;
        var owner = (await SignInAsync("bar.owner", Password))!;

        HttpClient At(Guid node)
        {
            var client = _api.CreateClient();
            client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
            client.DefaultRequestHeaders.Add("X-Gamma-Node", node.ToString());
            return client;
        }

        async Task<ElementDto> SaveAsync(HttpClient client, ElementDto element)
        {
            var saved = await client.PostAsJsonAsync("/api/v1/cost-centers", element, ct);
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(ct));
            return (await saved.Content.ReadFromJsonAsync<ElementDto>(ct))!;
        }

        // The company makes the bar, its hall and a table (all owned by the company).
        var atCompany = At(company);
        var types = (await atCompany.GetFromJsonAsync<ElementTypeDto[]>("/api/v1/cost-centers/types", ct))!;
        var (pos, hall, table) = (types.Single(t => t.Code == "POS_RESTAURANT"), types.Single(t => t.Code == "HALL"), types.Single(t => t.Code == "TABLE"));
        ElementDto Place(ElementTypeDto type, string code, Guid parent) => new()
        {
            TypeId = type.Id!.Value, IdSup = parent, Code = code, Name = new() { ["en"] = code }, CategoryIds = [type.CategoryIds[0]],
            Values = type.Properties.Where(p => p.Required).ToDictionary(p => p.Id!.Value, p => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create("x")),
        };
        var bar = await SaveAsync(atCompany, Place(pos, "BAR", company));
        var salla = await SaveAsync(atCompany, Place(hall, "HALL1", bar.Id!.Value));
        var t1 = await SaveAsync(atCompany, Place(table, "T1", salla.Id!.Value));
        var me = (await owner.GetFromJsonAsync<JsonElement>("/account/me", ct)).GetProperty("id").GetGuid();
        (await owner.PutAsJsonAsync($"/api/v1/users/{me}/cost-centers", new[] { company, bar.Id!.Value }, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        // B15: working at the bar, its own row and the places below it are seen and changed, though the company made them.
        var atBar = At(bar.Id!.Value);
        (await atBar.GetAsync($"/api/v1/cost-centers/{bar.Id}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var seen = (await atBar.GetFromJsonAsync<ElementDto>($"/api/v1/cost-centers/{t1.Id}", ct))!;
        var x = table.Properties.Single(p => p.Name["en"] == "Position X").Id!.Value;
        var moved = await SaveAsync(atBar, seen with { Values = new(seen.Values) { [x] = System.Text.Json.Nodes.JsonValue.Create(240) } });
        moved.Values[x]!.GetValue<decimal>().Should().Be(240);
        moved.OwnerId.Should().Be(company, "a change keeps who made it");
        var t2 = await SaveAsync(atBar, Place(table, "T2", salla.Id!.Value));
        t2.OwnerId.Should().Be(bar.Id, "what the bar makes is the bar's");
        var tables = (await atBar.GetFromJsonAsync<PageDto<ElementDto>>($"/api/v1/cost-centers?category={table.CategoryIds[0]}&type={table.Id}", ct))!;
        tables.Items.Select(t => t.Code).Should().BeEquivalentTo("T1", "T2");

        // Never above: the company itself is not the bar's to see.
        (await atBar.GetAsync($"/api/v1/cost-centers/{company}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Linked to the company only, the owner may still work at the bar below it; the entry page offers the companies only.
        (await atCompany.PutAsJsonAsync($"/api/v1/users/{me}/cost-centers", new[] { company }, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await At(bar.Id!.Value).GetAsync($"/api/v1/cost-centers/{t1.Id}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var offered = (await owner.GetFromJsonAsync<JsonElement>("/account/me", ct)).GetProperty("costCenters").EnumerateArray()
            .Select(c => (Id: c.GetProperty("id").GetGuid(), Linked: c.GetProperty("linked").GetBoolean())).ToList();
        offered.Should().Contain((company, true));
        offered.Select(o => o.Id).Should().NotContain([bar.Id!.Value, salla.Id!.Value, t1.Id!.Value], "the cost centres inside a company are not on the entry page");

        // Linked to the bar only: never above it.
        (await atCompany.PutAsJsonAsync($"/api/v1/users/{me}/cost-centers", new[] { bar.Id!.Value }, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await At(company).GetAsync("/api/v1/cost-centers/types", ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await At(salla.Id!.Value).GetAsync($"/api/v1/cost-centers/{t1.Id}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_group_keeps_its_own_books_a_new_one_from_the_sign_up_an_existing_one_set_up_once_at_the_group()
    {
        var ct = TestContext.Current.CancellationToken;
        // A new group: its books come with the sign-up, from the same country's pack.
        var made = await _api.Services.CreateCompanyAsync(new SignUpRequest("grupi.owner", Password, "AL", "Bar Lura", CorporateName: "Grupi Lura"), TestContext.Current.CancellationToken);
        var signedUp = made;
        var owner = (await SignInAsync("grupi.owner", Password))!;
        owner.DefaultRequestHeaders.Add("X-Gamma-Node", signedUp.Root.ToString());
        (await owner.GetFromJsonAsync<JsonElement>("/api/v1/group-books", ct)).GetProperty("needed").GetBoolean().Should().BeFalse();
        var groupTypes = await owner.GetFromJsonAsync<JsonElement[]>("/api/v1/documents/types", ct);
        groupTypes!.Should().NotBeEmpty("the group has its own document types");

        // An existing group without books (signed up before 2026-10-31): set up once at the group, by its administrator.
        await using var scope = _api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var tenantId = Guid.CreateVersion7();
        var companies = new[] { new Gamma.Erp.Seed.CompanySetup("TIRANA", "Tirana", "AL") };
        var organisation = Gamma.Erp.Seed.Organisation.Create(db, tenantId, "Grupi i vjetër", "AL", companies);
        Gamma.Erp.Seed.Organisation.SetUpCompany(db, organisation, companies[0]);
        await db.SaveChangesAsync(ct);
        var setup = scope.ServiceProvider.GetRequiredService<SetupService>();
        var atGroup = new Gamma.Erp.Services.RequestContext(tenantId, null, organisation.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        (await setup.GroupBooksNeededAsync(atGroup, ct)).Should().BeTrue();
        var atCompany = atGroup with { NodeId = organisation.Companies["TIRANA"] };
        var notThere = async () => await setup.SetUpGroupBooksAsync(new GroupBooksRequest("AL"), atCompany, ct);
        (await notThere.Should().ThrowAsync<Gamma.Erp.Services.ValidationException>()).Which.Errors.Should().ContainKey("rights");
        (await setup.SetUpGroupBooksAsync(new GroupBooksRequest("AL"), atGroup, ct)).Should().BePositive();
        (await setup.GroupBooksNeededAsync(atGroup, ct)).Should().BeFalse();
        var again = async () => await setup.SetUpGroupBooksAsync(new GroupBooksRequest("AL"), atGroup, ct);
        await again.Should().ThrowAsync<Gamma.Erp.Services.ValidationException>();

        // Each is the other's partner: at the group one for the company, at the company one for the group (made once, in both categories).
        async Task<List<Partner>> LinkedAsync(Guid tenant) => await db.Set<Partner>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.TenantId == tenant && p.GroupMemberId != null && p.Active).ToListAsync(ct);
        var tirana = organisation.Companies["TIRANA"];
        var old = await LinkedAsync(tenantId);
        old.Should().HaveCount(2);
        old.Should().ContainSingle(p => p.OwnerId == organisation.Corporate && p.GroupMemberId == tirana).Which.Name.In("en").Should().Be("Tirana");
        old.Should().ContainSingle(p => p.OwnerId == tirana && p.GroupMemberId == organisation.Corporate);
        var groupPartner = old.Single(p => p.OwnerId == organisation.Corporate);
        (await db.Set<InterPartnerCategory>().IgnoreQueryFilters().CountAsync(l => l.ElementId == groupPartner.Id && l.Active, ct)).Should().Be(2, "a client and a supplier");
        (await Gamma.Erp.Seed.Organisation.LinkGroupPartnersAsync(db, organisation, ct)).Should().Be(0, "made once");
        var fresh = await LinkedAsync(signedUp.TenantId);
        fresh.Should().ContainSingle(p => p.OwnerId == signedUp.Root && p.GroupMemberId == signedUp.Company);
        fresh.Should().ContainSingle(p => p.OwnerId == signedUp.Company && p.GroupMemberId == signedUp.Root);
    }
}

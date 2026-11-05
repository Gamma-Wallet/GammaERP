using System.Text.Json.Nodes;
using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

using Partners = EntityService<PartnerCategory, PartnerType, InterPartnerTypeCategory, PartnerTypeInfo, PartnerTypeInfoChoice, Partner, PartnerInfo,
    InterPartnerCategory, PartnerAccess>;

/// <summary>The entity rules, through the generic entity service, on partners of the example organisation.</summary>
public sealed class PartnerEntityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private OrganisationIds _org = null!;
    private Guid _clients;
    private Guid _sme;
    private Guid _admin;
    private Guid _seller;

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        await using var context = Context(null);
        await context.Database.MigrateAsync();
        CompanySetup[] companies = [new("ROMA", "Ristorante Roma", "IT"), new("TIRANA", "Fast Food Tirana", "AL"), new("LIMASSOL", "Supermarket Limassol", "CY")];
        _org = Organisation.Create(context, Guid.CreateVersion7(), "Gamma Group Inc.", "US", companies);
        foreach (var company in companies)
        {
            Organisation.SetUpCompany(context, _org, company);
        }

        await context.SaveChangesAsync();
        // Users are elements too: every row records who wrote it (created_by), so the test users are real users.
        var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = _org.TenantId };
        context.Add(staff);
        await context.SaveChangesAsync();
        var admin = new User { TypeId = staff.Id, Code = "ADMIN", Name = TranslatedText.English("Administrator"), TenantId = _org.TenantId };
        var seller = new User { TypeId = staff.Id, Code = "SELLER", Name = TranslatedText.English("Seller"), TenantId = _org.TenantId };
        context.AddRange(admin, seller);
        await context.SaveChangesAsync();
        (_admin, _seller) = (admin.Id, seller.Id);
        // Each company has its own rows from its pack.
        var tirana = _org.Companies["TIRANA"];
        _clients = (await context.Set<PartnerCategory>().SingleAsync(c => c.Code == "CLIENTS" && c.OwnerId == tirana)).Id;
        _sme = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "SME" && t.OwnerId == tirana)).Id;
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private ErpDbContext Context(RequestContext? request)
    {
        var tenant = new TenantContext();
        if (request is not null)
        {
            tenant.Set(request.TenantId, request.UserId);
        }

        var options = PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(), _db.GetConnectionString())
            .AddInterceptors(new TenantConnectionInterceptor(tenant), new HistoryInterceptor(tenant, TimeProvider.System));
        return new ErpDbContext((DbContextOptions<ErpDbContext>)options.Options);
    }

    private RequestContext At(string company, bool administrator = true, params Guid[] roles) =>
        new(_org.TenantId, administrator ? _admin : _seller, _org.Companies[company], roles.ToHashSet(), administrator);

    private static Partners Service(ErpDbContext db, RequestContext request) => new(db, request, new Visibility(db, request));

    private static Dictionary<string, string> Name(string name) => new() { ["en"] = name };

    private ElementDto Partner(string name, Guid? parent = null, Guid? type = null, Dictionary<Guid, JsonNode?>? values = null) => new()
    {
        Name = Name(name), TypeId = type ?? _sme, IdSup = parent, CategoryIds = [_clients], Values = values ?? [],
    };

    [Fact]
    public async Task A_partner_is_created_in_its_category_with_its_property_values_checked_by_kind()
    {
        var ct = TestContext.Current.CancellationToken;
        var tirana = At("TIRANA");
        await using var db = Context(tirana);
        var partners = Service(db, tirana);
        var sme = (await partners.TypesAsync(ct)).Single(t => t.Id == _sme);
        var withLimit = await partners.SaveTypeAsync(sme with
        {
            Properties = [new TypeInfoDto { Name = Name("Credit limit"), Kind = PropertyKind.Number, Required = true }],
        }, ct);
        var limit = withLimit.Properties.Single().Id!.Value;

        var wrong = () => partners.SaveElementAsync(Partner("Bar Lura", values: new() { [limit] = "a lot" }), ct);
        (await wrong.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey($"values.{limit}");
        var missing = () => partners.SaveElementAsync(Partner("Bar Lura"), ct);
        await missing.Should().ThrowAsync<ValidationException>("the credit limit is required");

        var saved = await partners.SaveElementAsync(Partner("Bar Lura", values: new() { [limit] = "5000.50" }), ct);
        saved.Values[limit]!.GetValue<decimal>().Should().Be(5000.50m);
        saved.CategoryIds.Should().Equal(_clients);
        saved.OwnerId.Should().Be(_org.Companies["TIRANA"], "a new element belongs to the request's node");
        (await partners.ElementsAsync(_clients, null, "lura", 1, 20, ct)).Items.Should().ContainSingle(p => p.Id == saved.Id);
    }

    [Fact]
    public async Task TypeError_an_elements_type_must_be_a_child_of_its_parents_type()
    {
        var ct = TestContext.Current.CancellationToken;
        var tirana = At("TIRANA");
        await using var db = Context(tirana);
        var partners = Service(db, tirana);
        var branch = await partners.SaveTypeAsync(new ElementTypeDto { Name = Name("Branch"), IdSup = _sme, IsElement = true, CategoryIds = [_clients] }, ct);
        var head = await partners.SaveElementAsync(Partner("Head office"), ct);

        var topBranch = () => partners.SaveElementAsync(Partner("Lonely branch", type: branch.Id), ct);
        (await topBranch.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.TypeError);
        // A row may sit under one of its own type, as cm under m (the founder, 2026-10-31).
        (await partners.SaveElementAsync(Partner("Same type", head.Id), ct)).IdSup.Should().Be(head.Id);

        var child = await partners.SaveElementAsync(Partner("Branch Durres", head.Id, branch.Id), ct);
        child.IdSup.Should().Be(head.Id);
        var cycle = () => partners.SaveElementAsync(head with { IdSup = child.Id }, ct);
        await cycle.Should().ThrowAsync<ValidationException>("an element cannot sit under one below it");
    }

    [Fact]
    public async Task NameError_ChildrenExistError_and_the_history_of_a_change()
    {
        var ct = TestContext.Current.CancellationToken;
        var tirana = At("TIRANA");
        await using var db = Context(tirana);
        var partners = Service(db, tirana);
        var shop = await partners.SaveTypeAsync(new ElementTypeDto { Name = Name("Shop"), IdSup = _sme, IsElement = true, CategoryIds = [_clients] }, ct);
        var first = await partners.SaveElementAsync(Partner("Kafe Flora"), ct);
        var twin = () => partners.SaveElementAsync(Partner("Kafe Flora"), ct);
        (await twin.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.NameError);

        var child = await partners.SaveElementAsync(Partner("Flora Blloku", first.Id, shop.Id), ct);
        var delete = () => partners.DeleteElementAsync(first.Id!.Value, ct);
        (await delete.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.ChildrenExistError);

        await partners.SaveElementAsync(first with { Name = Name("Kafe Flora 2") }, ct);
        var history = await partners.HistoryAsync(first.Id!.Value, ct);
        history.Should().ContainSingle();
        history[0].Row["Name"]!["en"]!.GetValue<string>().Should().Be("Kafe Flora");

        await partners.DeleteElementAsync(child.Id!.Value, ct);
        (await partners.ElementAsync(child.Id!.Value, ct)).Should().BeNull("a deleted element is switched off and no longer read");
    }

    [Fact]
    public async Task Lists_are_paged_in_the_database_like_the_old_Page_and_Safaqe()
    {
        var ct = TestContext.Current.CancellationToken;
        var tirana = At("TIRANA");
        await using var db = Context(tirana);
        var partners = Service(db, tirana);
        var made = new List<Guid>();
        foreach (var name in new[] { "Alfa", "Beta", "Gama", "Delta", "100% Fresh" })
        {
            made.Add((await partners.SaveElementAsync(Partner(name), ct)).Id!.Value);
        }

        var last = await partners.ElementsAsync(_clients, null, null, 3, 2, ct);
        (last.Total, last.PageCount, last.Page).Should().Be((5, 3, 3), "five rows at two a page are three pages");
        last.Items.Select(p => p.Id!.Value).Should().Equal(made[4]);
        (await partners.ElementsAsync(_clients, null, null, 1, 2, ct)).Items.Select(p => p.Id!.Value).Should().Equal(made[0], made[1]);
        (await partners.ElementsAsync(_clients, null, null, 0, 0, ct)).Items.Should().ContainSingle("page and size are kept to at least 1");
        (await partners.ElementsAsync(_clients, null, "a", 1, 50, ct)).Total.Should().Be(4);
        (await partners.ElementsAsync(_clients, null, "%", 1, 50, ct)).Items.Should().ContainSingle(p => p.Id == made[4], "a % in the search is a literal %");
    }

    [Fact]
    public async Task A_node_never_sees_what_another_company_owns()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid id;
        var tirana = At("TIRANA");
        await using (var db = Context(tirana))
        {
            id = (await Service(db, tirana).SaveElementAsync(Partner("Tirana only"), ct)).Id!.Value;
        }

        var roma = At("ROMA");
        await using var other = Context(roma);
        var partners = Service(other, roma);
        (await partners.ElementAsync(id, ct)).Should().BeNull();
        (await partners.CategoriesAsync(ct)).Select(c => c.Id).Should().NotContain(_clients, "Roma has its own categories, never Fast Food Tirana's");
    }

    [Fact]
    public async Task Read_own_shows_only_the_partners_given_to_the_user_or_the_users_role()
    {
        var ct = TestContext.Current.CancellationToken;
        var sellers = Guid.CreateVersion7();
        Guid mine, other;
        var admin = At("TIRANA");
        await using (var db = Context(admin))
        {
            var role = new UserCategory { Id = sellers, Code = "SELLER_TEST", Name = TranslatedText.English("Seller (test)"), TenantId = _org.TenantId };
            db.Add(role);
            await db.SaveChangesAsync(ct);
            var partners = Service(db, admin);
            mine = (await partners.SaveElementAsync(Partner("Given to sellers") with { AccessRoleIds = [sellers] }, ct)).Id!.Value;
            other = (await partners.SaveElementAsync(Partner("Not given"), ct)).Id!.Value;
            var entity = new Elements { Table = "tbl_partner", Name = TranslatedText.English("Partners"), TenantId = _org.TenantId };
            db.Add(entity);
            db.Add(new ElementsAccess { RoleId = sellers, ElementsId = entity.Id, CategoryId = _clients, ReadOwn = true, TenantId = _org.TenantId });
            await db.SaveChangesAsync(ct);
        }

        var user = At("TIRANA", administrator: false, roles: sellers);
        await using var read = Context(user);
        var list = await Service(read, user).ElementsAsync(_clients, null, null, 1, 50, ct);
        list.Items.Select(p => p.Id).Should().Contain(mine).And.NotContain(other);
        (await Service(read, user).ElementAsync(other, ct)).Should().BeNull();
        var write = () => Service(read, user).SaveElementAsync(Partner("No rights to write"), ct);
        (await write.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("rights");
    }
}

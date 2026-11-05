using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>
/// The menu of an Albanian company from its pack: the administrator sees all of it; a seller sees the folder given to their role with what is
/// under it, a report given to them alone with the folders above it, and of the documents only the types they were given; a new type shows
/// without touching the menu.
/// </summary>
public sealed class MenuTests : IAsyncLifetime
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
    public async Task Only_an_administrator_sees_the_whole_menu_gives_it_and_a_node_for_administrators_is_never_offered()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", [("cost_center_type:WAREHOUSE", "WH", "Magazina")]);
        OrganisationIds company;
        Guid user, role;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            var seller = new UserCategory { Code = "SELLERS", Name = TranslatedText.English("Sellers"), IsElement = true, TenantId = tenantId };
            db.AddRange(staff, seller);
            await db.SaveChangesAsync(ct);
            var person = new User { TypeId = staff.Id, Code = "ARDI", Name = TranslatedText.English("Ardi"), TenantId = tenantId };
            db.Add(person);
            await db.SaveChangesAsync(ct);
            (user, role) = (person.Id, seller.Id);
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        await using var context = Context(tenant);
        var admin = new RequestContext(tenantId, null, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        var seller2 = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid> { role });
        var rights = new MenuRightsService(context, admin, new Visibility(context, admin));
        var nodes = await context.Set<Menu>().AsNoTracking().ToListAsync(ct);

        var offered = await rights.RightsAsync(null, role, "en", ct);
        offered.Should().OnlyContain(n => !n.Given);
        offered.Select(n => n.Code).Should().Contain("REPORTS").And.NotContain(["USER_RIGHTS", "MENU"], "those are for administrators");
        nodes.Should().NotContain(n => n.Code == "MENU_ACCESS", "Rights does its job");
        (await new MenuService(new Visibility(context, seller2), new DocumentService(context, seller2, new Visibility(context, seller2), TimeProvider.System)).MenuAsync("en", ct)).Should().BeEmpty("nothing was given");

        var refused = () => new MenuRightsService(context, seller2, new Visibility(context, seller2)).RightsAsync(null, role, "en", ct);
        (await refused.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("rights");
        var configuration = () => new Gamma.Erp.Services.Configuration.ConfigurationService(context, seller2, new Visibility(context, seller2)).ListAsync("menus", new Dictionary<string, string>(), ct);
        (await configuration.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("rights");

        var system = nodes.Single(n => n.Code == "SYSTEM_SETTINGS").Id;
        await rights.SetAsync(null, role, [system], ct);
        context.ChangeTracker.Clear();
        (await rights.RightsAsync(null, role, "en", ct)).Single(n => n.Given).Id.Should().Be(system);
        var shown = await new MenuService(new Visibility(context, seller2), new DocumentService(context, seller2, new Visibility(context, seller2), TimeProvider.System)).MenuAsync("en", ct);
        shown.Single().Children.Select(n => n.Code).Should().Contain("MIGRATION").And.NotContain("MENU", "a folder gives what's under it, except a node for administrators");

        var forbidden = () => rights.SetAsync(null, role, [nodes.Single(n => n.Code == "MENU").Id], ct);
        (await forbidden.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("menu");
        await rights.SetAsync(null, role, [], ct);
        context.ChangeTracker.Clear();
        (await rights.RightsAsync(null, role, "en", ct)).Should().OnlyContain(n => !n.Given, "the list is exactly what was saved");
    }

    [Fact]
    public async Task The_menu_comes_from_the_pack_and_shows_each_user_what_they_or_their_roles_were_given()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", [("cost_center_type:WAREHOUSE", "WH", "Magazina")]);
        OrganisationIds company;
        Guid owner, seller;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            db.Add(staff);
            await db.SaveChangesAsync(ct);
            var users = ((string[])["OWNER", "ARDI"]).Select(c => new User { TypeId = staff.Id, Code = c, Name = TranslatedText.English(c), TenantId = tenantId }).ToList();
            db.AddRange(users);
            await db.SaveChangesAsync(ct);
            (owner, seller) = (users[0].Id, users[1].Id);
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, owner);
        await using var context = Context(tenant);
        var admin = new RequestContext(tenantId, owner, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        var adminDocuments = new DocumentService(context, admin, new Visibility(context, admin), TimeProvider.System);
        var everything = await new MenuService(new Visibility(context, admin), adminDocuments).MenuAsync("sq", ct);
        everything.Select(n => n.Code).Should().Equal("DOCUMENTS", "PARTNERS", "POS", "PRODUCTS_AREA", "STOCK_AREA", "ACCOUNTING_SETUP", "FIXED_ASSETS", "CURRENCIES_AREA", "REPORTS", "COMPANY",
            "USERS_AREA", "SYSTEM_SETTINGS");
        everything[0].Name.Should().Be("Dokumente");
        var sales = everything[0].Children.Single(n => n.Code == "DOCUMENTS_SALES");
        sales.Screen.Should().Be("documents");
        sales.DocumentTypes.Select(t => t.Code).Should().Contain("FSHM");

        // The seller's role gets the documents folder, the seller alone the trial balance, and the role one sales type.
        var role = (await context.Set<UserCategory>().SingleAsync(c => c.Code == "SELLER", ct)).Id;
        var nodes = await context.Set<Menu>().ToDictionaryAsync(m => m.Code!, m => m.Id, ct);
        context.AddRange(
            new InterMenuUser { MenuId = nodes["DOCUMENTS"], RoleId = role, TenantId = tenantId, OwnerId = company.Corporate },
            new InterMenuUser { MenuId = nodes["TRIAL_BALANCE"], UserId = seller, TenantId = tenantId, OwnerId = company.Corporate });
        await context.SaveChangesAsync(ct);
        var types = await context.Set<DocumentType>().ToListAsync(ct);
        await adminDocuments.SaveTypeUsersAsync(types.Single(t => t.Code == "FSHM").Id, [], [role], ct);
        context.ChangeTracker.Clear();

        var request = new RequestContext(tenantId, seller, company.Corporate, new HashSet<Guid> { role });
        async Task<IReadOnlyList<MenuNodeDto>> SellersMenuAsync() =>
            await new MenuService(new Visibility(context, request), new DocumentService(context, request, new Visibility(context, request), TimeProvider.System))
                .MenuAsync("en", ct);
        var menu = await SellersMenuAsync();
        menu.Select(n => n.Code).Should().Equal("DOCUMENTS", "REPORTS");
        menu[0].Children.Select(n => n.Code).Should().Equal(["DOCUMENTS_SALES"], "a documents item with none of the seller's types is left out");
        menu[0].Children[0].DocumentTypes.Select(t => t.Code).Should().Equal("FSHM");
        menu[1].Children.Should().ContainSingle().Which.Code.Should().Be("FINANCIAL_REPORTS");
        menu[1].Children[0].Children.Select(n => n.Code).Should().Equal("TRIAL_BALANCE");

        // A new sales type given to the role shows in the same item, the menu untouched.
        var category = (await context.Set<DocumentCategory>().SingleAsync(c => c.Code == "SALES", ct)).Id;
        var fresh = await adminDocuments.Entities.SaveTypeAsync(new ElementTypeDto
        {
            Code = "FSHE", Name = new Dictionary<string, string> { ["en"] = "Event sale" }, IsElement = true, SortOrder = 999, CategoryIds = [category],
        }, ct);
        await adminDocuments.SaveTypeUsersAsync(fresh.Id!.Value, [], [role], ct);
        context.ChangeTracker.Clear();
        (await SellersMenuAsync())[0].Children[0].DocumentTypes.Select(t => t.Code).Should().Equal("FSHM", "FSHE");
    }
}

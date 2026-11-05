using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Audit;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>User activity: read from the history every row keeps, opened by Menu access, never showing an id or a password.</summary>
public sealed class AuditTests : IAsyncLifetime
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
    public async Task Added_changed_and_deleted_are_read_from_the_history_by_user_with_names_and_no_password()
    {
        var ct = TestContext.Current.CancellationToken;
        var (tenantId, owner) = await CompanyAsync(ct);
        var tenant = new TenantContext();
        tenant.Set(tenantId, null);
        await using var context = Context(tenant);
        var ana = await UserAsync(context, tenantId, owner, "ana", ct);
        var ben = await UserAsync(context, tenantId, owner, "ben", ct);
        var types = await context.Set<PartnerType>().Where(t => t.OwnerId == owner).OrderBy(t => t.Code).Take(2).ToListAsync(ct);

        tenant.Set(tenantId, ana.Id);
        var partner = new Partner { TypeId = types[0].Id, Code = "C1", Name = TranslatedText.English("Bar Lura"), TenantId = tenantId, OwnerId = owner };
        var gone = new Partner { TypeId = types[0].Id, Code = "C2", Name = TranslatedText.English("Bar Vjeter"), TenantId = tenantId, OwnerId = owner };
        context.AddRange(partner, gone);
        await context.SaveChangesAsync(ct);

        tenant.Set(tenantId, ben.Id);
        partner.Name = TranslatedText.English("Bar Lura e Re");
        partner.TypeId = types[1].Id;
        context.Remove(gone);
        ana.PasswordHash = "a-new-hash";
        await context.SaveChangesAsync(ct);

        var audit = new AuditService(context, new Visibility(context, new RequestContext(tenantId, ben.Id, owner, new HashSet<Guid>(), IsAdministrator: true)));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var byAna = await audit.ActivityAsync(today, today, ana.Id, ct);
        byAna.Should().ContainSingle(t => t.Table == nameof(Partner)).Which.Should().BeEquivalentTo(new { Label = "Partner", Added = 2, Changed = 0, Deleted = 0 });
        var byBen = await audit.ActivityAsync(today, today, ben.Id, ct);
        byBen.Should().ContainSingle(t => t.Table == nameof(Partner)).Which.Should().BeEquivalentTo(new { Added = 0, Changed = 1, Deleted = 1 });
        (await audit.ActivityAsync(today.AddDays(-3), today.AddDays(-1), ben.Id, ct)).Should().BeEmpty();

        var rows = await audit.RowsAsync(nameof(Partner), today, today, ben.Id, "en", ct);
        rows.Should().HaveCount(2);
        rows.Should().ContainSingle(r => r.Action == "Deleted").Which.Record.Should().Be("C2 Bar Vjeter");
        var changed = rows.Single(r => r.Action == "Changed");
        changed.User.Should().Be("ben");

        var fields = await audit.ChangeAsync(nameof(Partner), changed.Id, "en", ct);
        fields.Should().ContainSingle(f => f.Field == "Name").Which.Should().BeEquivalentTo(new { Before = "Bar Lura", After = "Bar Lura e Re" });
        fields.Should().ContainSingle(f => f.Field == "Type").Which.Before.Should().Contain(types[0].Code!, "an id is shown by its name");
        fields.Should().HaveCount(2);

        var deleted = await audit.ChangeAsync(nameof(Partner), rows.Single(r => r.Action == "Deleted").Id, "en", ct);
        deleted.Should().ContainSingle(f => f.Field == "Name").Which.Should().BeEquivalentTo(new { Before = "Bar Vjeter", After = (string?)null });

        var userChange = (await audit.RowsAsync(nameof(User), today, today, ben.Id, "en", ct)).Single();
        (await audit.ChangeAsync(nameof(User), userChange.Id, "en", ct)).Should().BeEmpty("a password's hash is never shown");
        var act = () => audit.RowsAsync("users; drop", today, today, null, "en", ct);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Menu_access_opens_it_for_a_role_given_the_folder_above_and_the_server_refuses_anyone_else()
    {
        var ct = TestContext.Current.CancellationToken;
        var (tenantId, owner) = await CompanyAsync(ct);
        var tenant = new TenantContext();
        tenant.Set(tenantId, null);
        await using var context = Context(tenant);
        var user = await UserAsync(context, tenantId, owner, "dea", ct);
        var category = new UserCategory { Code = "AUDIT", Name = TranslatedText.English("Audit"), IsElement = true, TenantId = tenantId };
        context.Add(category);
        await context.SaveChangesAsync(ct);
        var role = category.Id;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var screen = await context.Set<Menu>().SingleAsync(m => m.OwnerId == owner && m.Screen == AuditService.Screen, ct);
        context.Add(new Login { UserId = user.Id, InOut = false, Failed = true, OwnerId = owner, TenantId = tenantId });
        context.Add(new Login { UserId = user.Id, InOut = true, OwnerId = owner, TenantId = tenantId });
        await context.SaveChangesAsync(ct);

        AuditService Audit() => new(context, new Visibility(context, new RequestContext(tenantId, user.Id, owner, new HashSet<Guid> { role })));

        var refused = () => Audit().SignInsAsync(today, today, null, ct);
        (await refused.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("rights");
        var refusedAct = () => Audit().ActivityAsync(today, today, null, ct);
        (await refusedAct.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("rights");

        context.Add(new InterMenuUser { MenuId = screen.IdSup, RoleId = role, OwnerId = owner, TenantId = tenantId });
        await context.SaveChangesAsync(ct);

        var signIns = await Audit().SignInsAsync(today, today, null, ct);
        signIns.Select(s => s.Event).Should().BeEquivalentTo(["Wrong password", "Signed in"]);
        signIns.Should().OnlyContain(s => s.User == "dea");
    }

    private async Task<(Guid TenantId, Guid Owner)> CompanyAsync(CancellationToken ct)
    {
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", [("cost_center_type:WAREHOUSE", "WH1", "Magazina 1")]);
        await using var db = Context(new TenantContext());
        var company = Organisation.CreateCompany(db, tenantId, setup);
        Organisation.SetUpCompany(db, company, setup);
        await db.SaveChangesAsync(ct);
        return (tenantId, company.Corporate);
    }

    private static async Task<User> UserAsync(ErpDbContext context, Guid tenantId, Guid owner, string name, CancellationToken ct)
    {
        var type = await context.Set<UserType>().FirstOrDefaultAsync(ct);
        if (type is null)
        {
            type = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            context.Add(type);
            await context.SaveChangesAsync(ct);
        }

        var user =new User { TypeId = type.Id, Code = name, Name = TranslatedText.English(name), Username = name, PasswordHash = "a-hash", TenantId = tenantId, OwnerId = owner };
        context.Add(user);
        await context.SaveChangesAsync(ct);
        return user;
    }
}

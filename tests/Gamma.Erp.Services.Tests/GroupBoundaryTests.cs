using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>
/// The group and its companies (the founder, 2026-10-31): the group behaves as a company of its own and sees only its rows and its own cost
/// centres; a company sees its whole tree and never the group's rows. What passes between them goes by documents.
/// </summary>
public sealed class GroupBoundaryTests : IAsyncLifetime
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
    public async Task The_group_sees_only_its_own_rows_and_a_company_its_whole_tree_but_never_the_groups()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setups = new[] { new CompanySetup("TIRANA", "Tirana", "AL", [("cost_center_type:WAREHOUSE", "WH-T", "Magazina")]), new CompanySetup("DURRES", "Durrës", "AL") };
        OrganisationIds group;
        await using (var db = Context(new TenantContext()))
        {
            group = Organisation.Create(db, tenantId, "Grupi", "AL", setups);
            foreach (var setup in setups)
            {
                Organisation.SetUpCompany(db, group, setup);
            }

            await db.SaveChangesAsync(ct);
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, null);
        await using var context = Context(tenant);
        var (tirana, durres) = (group.Companies["TIRANA"], group.Companies["DURRES"]);
        var warehouse = await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH-T", ct);
        // The group's own office, and a unit made at the group.
        var office = new CostCenter { TypeId = warehouse.TypeId, IdSup = group.Corporate, Code = "HQ", Name = TranslatedText.English("Zyra qendrore"), TenantId = tenantId,
            OwnerId = group.Corporate };
        var unit = await context.Set<Unit>().FirstAsync(u => u.OwnerId == tirana, ct);
        var groupUnit = new Unit { TypeId = unit.TypeId, Code = "PALLET", Name = TranslatedText.English("Pallet"), TenantId = tenantId, OwnerId = group.Corporate };
        context.AddRange(office, groupUnit);
        await context.SaveChangesAsync(ct);

        Visibility At(Guid node) => new(context, new RequestContext(tenantId, null, node, new HashSet<Guid>(), IsAdministrator: true));

        // The group: its own node and office; no company, nothing inside one; it is its own company.
        var atGroup = At(group.Corporate);
        (await atGroup.NodesAsync(ct)).Should().BeEquivalentTo([group.Corporate, office.Id]);
        (await atGroup.CompanyAsync(ct)).Should().Be(group.Corporate, "the group behaves as a company");
        var groupConfig = await atGroup.ConfigurationNodesAsync(ct);
        groupConfig.Should().NotContain([tirana, durres, warehouse.Id]);
        (await context.Set<Unit>().Where(u => u.OwnerId != null && groupConfig.Contains(u.OwnerId.Value)).Select(u => u.Code).ToListAsync(ct))
            .Should().Equal(["PALLET"], "the companies' units are theirs");

        // A company: its whole tree, never the group's office or units, nor the other company.
        var atTirana = At(tirana);
        (await atTirana.NodesAsync(ct)).Should().Contain([tirana, warehouse.Id]).And.NotContain([group.Corporate, office.Id, durres]);
        var tiranaConfig = await atTirana.ConfigurationNodesAsync(ct);
        tiranaConfig.Should().NotContain([group.Corporate, durres]);
        (await context.Set<Unit>().Where(u => u.OwnerId != null && tiranaConfig.Contains(u.OwnerId.Value)).Select(u => u.Code).ToListAsync(ct))
            .Should().NotContain("PALLET").And.NotBeEmpty();
        (await atTirana.CompanyAsync(ct)).Should().Be(tirana);
        (await At(warehouse.Id).CompanyAsync(ct)).Should().Be(tirana);
    }
}

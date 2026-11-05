using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Persistence.Tests;

/// <summary>The history rules, kept by the database layer for every table.</summary>
public sealed class HistoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly Guid _tenant = Guid.CreateVersion7();

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        await using var context = Context(withTenant: false);
        await context.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private ErpDbContext Context(bool withTenant = true)
    {
        var tenant = new TenantContext();
        if (withTenant)
        {
            tenant.Set(_tenant, null);
        }

        var options = PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(), _db.GetConnectionString())
            .AddInterceptors(new TenantConnectionInterceptor(tenant), new HistoryInterceptor(tenant, TimeProvider.System));
        return new ErpDbContext((DbContextOptions<ErpDbContext>)options.Options);
    }

    private async Task<(Guid Type, Guid Partner)> PartnerAsync(string name)
    {
        await using var context = Context();
        var type = new PartnerType { Code = "SME", Name = TranslatedText.English("SME"), IsElement = true };
        context.Add(type);
        var partner = new Partner { TypeId = type.Id, Code = "P1", Name = TranslatedText.English(name) };
        context.Add(partner);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (type.Id, partner.Id);
    }

    [Fact]
    public async Task A_change_keeps_the_row_and_copies_its_old_state_into_a_disabled_row_of_the_same_table()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, id) = await PartnerAsync("Old name");

        await using (var context = Context())
        {
            var partner = await context.Set<Partner>().SingleAsync(p => p.Id == id, ct);
            partner.Name = TranslatedText.English("New name");
            await context.SaveChangesAsync(ct);
        }

        await using (var context = Context())
        {
            var partner = await context.Set<Partner>().SingleAsync(p => p.Id == id, ct);
            partner.Name.In("en").Should().Be("New name", "the row keeps its id and takes the change");
            partner.IdChange.Should().NotBeNull();
            var copy = await context.Set<Partner>().IgnoreQueryFilters().SingleAsync(p => p.Id == partner.IdChange, ct);
            copy.Active.Should().BeFalse();
            copy.Name.In("en").Should().Be("Old name");
            copy.IdChange.Should().BeNull("it is the first change");
            (await context.Set<Partner>().CountAsync(ct)).Should().Be(1, "reads see only active rows");
        }
    }

    [Fact]
    public async Task Each_change_chains_to_the_one_before()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, id) = await PartnerAsync("v1");
        foreach (var name in new[] { "v2", "v3" })
        {
            await using var context = Context();
            (await context.Set<Partner>().SingleAsync(p => p.Id == id, ct)).Name = TranslatedText.English(name);
            await context.SaveChangesAsync(ct);
        }

        await using var read = Context();
        var all = await read.Set<Partner>().IgnoreQueryFilters().ToDictionaryAsync(p => p.Id, ct);
        var names = new List<string?>();
        for (Guid? cursor = id; cursor is { } c; cursor = all[c].IdChange)
        {
            names.Add(all[c].Name.In("en"));
        }

        names.Should().Equal("v3", "v2", "v1");
    }

    [Fact]
    public async Task Nothing_is_deleted_a_delete_switches_the_row_off()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, id) = await PartnerAsync("To delete");
        await using (var context = Context())
        {
            context.Remove(await context.Set<Partner>().SingleAsync(p => p.Id == id, ct));
            await context.SaveChangesAsync(ct);
        }

        await using var read = Context();
        (await read.Set<Partner>().AnyAsync(p => p.Id == id, ct)).Should().BeFalse("deleted rows are not read");
        var row = await read.Set<Partner>().IgnoreQueryFilters().SingleAsync(p => p.Id == id, ct);
        row.Active.Should().BeFalse("it is still there, switched off");
    }

    [Fact]
    public async Task The_database_refuses_a_delete_and_another_tenants_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, id) = await PartnerAsync("Kept");
        await using var context = Context();
        var delete = () => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM partners.tbl_partner WHERE id = {id}", ct);
        (await delete.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("42501", "permission denied: requests have no DELETE right");

        var stranger = new TenantContext();
        stranger.Set(Guid.CreateVersion7(), null);
        var options = PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(), _db.GetConnectionString())
            .AddInterceptors(new TenantConnectionInterceptor(stranger), new HistoryInterceptor(stranger, TimeProvider.System));
        await using var foreign = new ErpDbContext((DbContextOptions<ErpDbContext>)options.Options);
        (await foreign.Set<Partner>().IgnoreQueryFilters().AnyAsync(p => p.Id == id, ct)).Should().BeFalse("another corporate never sees the row");
    }
}

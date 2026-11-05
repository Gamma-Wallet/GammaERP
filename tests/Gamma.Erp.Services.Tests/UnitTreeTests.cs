using System.Text.Json.Nodes;
using AwesomeAssertions;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Configuration;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>Units as trees (the founder, 2026-10-31): a base unit with the units under it; a conversion only within one tree, never cm to kg.</summary>
public sealed class UnitTreeTests : IAsyncLifetime
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
    public async Task The_pack_gives_units_as_trees_and_a_conversion_is_only_between_units_of_one_tree()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Tregtia", "IT");
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
        var request = new RequestContext(tenantId, null, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        var visibility = new Visibility(context, request);
        var units = await context.Set<Unit>().ToDictionaryAsync(u => u.Code!, ct);
        units["CM"].IdSup.Should().Be(units["M"].Id);
        units["MM"].IdSup.Should().Be(units["M"].Id);
        units["G"].IdSup.Should().Be(units["KG"].Id);
        units["M"].IdSup.Should().BeNull();
        // Time is one tree (the founder, 2026-10-31): year › month › day › hour.
        units["DAY"].IdSup.Should().Be(units["MONTH"].Id);
        units["HOUR"].IdSup.Should().Be(units["DAY"].Id);
        (await UnitConversionService.SameTreeAsync(context, units["HOUR"].Id, units["YEAR"].Id, ct)).Should().BeTrue();

        (await UnitConversionService.SameTreeAsync(context, units["MM"].Id, units["KM"].Id, ct)).Should().BeTrue("both under m");
        (await UnitConversionService.SameTreeAsync(context, units["CM"].Id, units["KG"].Id, ct)).Should().BeFalse();
        var conversions = new UnitConversionService(context, request, visibility, TimeProvider.System);
        (await conversions.FactorAsync(units["M"].Id, units["MM"].Id, null, ct)).Should().Be(1000);
        (await conversions.FactorAsync(units["CM"].Id, units["KG"].Id, null, ct)).Should().BeNull();

        var config = new ConfigurationService(context, request, visibility);
        var wrong = async () => await config.SaveAsync("unit-conversions", new JsonObject
        {
            ["fromUnitId"] = units["CM"].Id.ToString(), ["toUnitId"] = units["KG"].Id.ToString(), ["factor"] = 2,
        }, ct);
        (await wrong.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("toUnitId");
        var right = await config.SaveAsync("unit-conversions", new JsonObject
        {
            ["fromUnitId"] = units["KM"].Id.ToString(), ["toUnitId"] = units["CM"].Id.ToString(), ["factor"] = 100000,
        }, ct);
        right["factor"]!.GetValue<decimal>().Should().Be(100000);

        // One product's own conversion may cross trees: a pack (a piece) of this coffee is 250 g.
        var coffee = new Gamma.Erp.Models.Products.Product { TypeId = (await context.Set<Gamma.Erp.Models.Products.ProductType>().FirstAsync(ct)).Id, Code = "COFFEE",
            Name = Gamma.Erp.Models.Base.TranslatedText.English("Kafe"), TenantId = tenantId, OwnerId = company.Corporate };
        context.Add(coffee);
        await context.SaveChangesAsync(ct);
        await config.SaveAsync("unit-conversions", new JsonObject
        {
            ["fromUnitId"] = units["PIECE"].Id.ToString(), ["toUnitId"] = units["G"].Id.ToString(), ["factor"] = 250, ["productId"] = coffee.Id.ToString(),
        }, ct);
        (await conversions.FactorAsync(units["PIECE"].Id, units["G"].Id, coffee.Id, ct)).Should().Be(250);
        (await conversions.FactorAsync(units["PIECE"].Id, units["G"].Id, null, ct)).Should().BeNull("only that product's");

        // In the register a unit goes under one of its own type (dm under m); never under one of another tree's type.
        var register = new EntityService<UnitCategory, UnitType, InterUnitTypeCategory, UnitTypeInfo, UnitTypeInfoChoice, Unit, UnitInfo, InterUnitCategory, UnitAccess>(
            context, request, visibility);
        var length = await context.Set<InterUnitCategory>().Where(l => l.ElementId == units["M"].Id).Select(l => l.CategoryId).FirstAsync(ct);
        var dm = await register.SaveElementAsync(new Gamma.Erp.Models.Base.Dto.ElementDto
        {
            TypeId = units["M"].TypeId, IdSup = units["M"].Id, Code = "DM", Name = new() { ["en"] = "dm" }, CategoryIds = [length],
        }, ct);
        dm.IdSup.Should().Be(units["M"].Id);
        var underKg = async () => await register.SaveElementAsync(new Gamma.Erp.Models.Base.Dto.ElementDto
        {
            TypeId = units["M"].TypeId, IdSup = units["KG"].Id, Code = "XX", Name = new() { ["en"] = "xx" }, CategoryIds = [length],
        }, ct);
        (await underKg.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("typeId");
    }
}

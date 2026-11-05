using AwesomeAssertions;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Posting;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Persistence.Tests;

/// <summary>The organisation and the Albanian starter pack load into a real database, every link checked by it.</summary>
public sealed class SeedTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        await using var context = Context();
        await context.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private ErpDbContext Context()
    {
        var tenant = new TenantContext();
        var options = PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(), _db.GetConnectionString())
            .AddInterceptors(new TenantConnectionInterceptor(tenant), new HistoryInterceptor(tenant, TimeProvider.System));
        return new ErpDbContext((DbContextOptions<ErpDbContext>)options.Options);
    }

    [Fact]
    public async Task Each_company_gets_its_countrys_pack_and_its_cost_centres()
    {
        var ct = TestContext.Current.CancellationToken;
        CompanySetup[] companies =
        [
            new("ROMA", "Ristorante Roma", "IT"),
            new("TIRANA", "Fast Food Tirana", "AL", [("cost_center_type:BASE", "BASE", "Base"), ("cost_center_type:CASH_DESK", "CASH", "Cash desk"),
                ("cost_center_type:BANK", "BANK", "Bank"), ("cost_center_type:POS_RESTAURANT", "POS1", "Point of sale Blloku"), ("cost_center_type:WAREHOUSE", "WH1", "Warehouse")]),
            new("LIMASSOL", "Supermarket Limassol", "CY"),
        ];
        OrganisationIds organisation;
        await using (var context = Context())
        {
            organisation = Organisation.Create(context, Guid.CreateVersion7(), "Gamma Group Inc.", "US", companies);
            foreach (var company in companies)
            {
                Organisation.SetUpCompany(context, organisation, company);
            }

            await context.SaveChangesAsync(ct);
        }

        await using var read = Context();
        var tirana = organisation.Companies["TIRANA"];
        var roma = organisation.Companies["ROMA"];
        var limassol = organisation.Companies["LIMASSOL"];
        (await read.Set<DocumentType>().CountAsync(t => t.OwnerId == tirana, ct)).Should().Be(93, "the base and the opening stock types of a country with an opening account");
        (await read.Set<DocumentType>().AnyAsync(t => t.OwnerId == tirana && t.Code == "FSHM", ct)).Should().BeTrue("Albania's codes");
        (await read.Set<DocumentType>().CountAsync(t => t.OwnerId == roma, ct)).Should().Be(91, "the same base, in Italian");
        (await read.Set<DocumentType>().AnyAsync(t => t.OwnerId == roma && t.Code == "FVM", ct)).Should().BeTrue("Italy's codes");
        (await read.Set<DocumentType>().CountAsync(t => t.OwnerId == limassol, ct)).Should().Be(91, "the same base, in Greek");
        (await read.Set<DocumentType>().AnyAsync(t => t.OwnerId == limassol && t.Code == "INV", ct)).Should().BeTrue("Cyprus's codes");
        (await read.Set<TransactionType>().CountAsync(t => t.OwnerId == tirana, ct)).Should().Be(22);
        (await read.Set<Scenario>().CountAsync(s => s.OwnerId == tirana, ct)).Should().Be(364, "scenarios by class of account");
        (await read.Set<Scenario>().CountAsync(s => s.OwnerId == roma, ct)).Should().Be(358, "Italy's inventories and expenses are one class");
        (await read.Set<CostCenter>().CountAsync(c => c.IdSup == tirana, ct)).Should().Be(5);
        (await read.Set<CostCenter>().CountAsync(c => c.IdSup == organisation.Corporate, ct)).Should().Be(3, "three companies under the corporate");
        (await read.Set<CostCenter>().CountAsync(c => c.OwnerId == roma, ct)).Should().Be(1, "Roma owns only itself so far");
        var types = await read.Set<CostCenterType>().ToListAsync(ct);
        types.Single(t => t.Code == "COMPANY").IdSup.Should().Be(organisation.CorporateType, "the types form the same tree");
        types.Where(t => t.OwnerId == tirana).Should().OnlyContain(t => t.IdSup == organisation.CompanyType || types.Any(p => p.Id == t.IdSup && p.OwnerId == tirana));
    }
}

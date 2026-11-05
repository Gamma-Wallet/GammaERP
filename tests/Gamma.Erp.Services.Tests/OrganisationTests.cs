using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Posting;
using Gamma.Erp.Models.Transactions;
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

using CostCenters = EntityService<CostCenterCategory, CostCenterType, InterCostCenterTypeCategory, CostCenterTypeInfo, CostCenterTypeInfoChoice, CostCenter,
    CostCenterInfo, InterCostCenterCategory, CostCenterAccess>;

/// <summary>The cost centre tree: a company is known by its type (it keeps its own books), the root of its tree or directly under it.</summary>
public sealed class OrganisationTests : IAsyncLifetime
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
    public async Task CompanyLevelError_a_company_is_its_own_root_or_directly_under_it_never_under_a_company()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        OrganisationIds shop;
        Guid user;
        await using (var context = Context(new TenantContext()))
        {
            shop = Organisation.CreateCompany(context, tenantId, new CompanySetup("ARTI", "Berberia Arti", "AL"));
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            context.Add(staff);
            await context.SaveChangesAsync(ct);
            var owner = new User { TypeId = staff.Id, Code = "BARBER", Name = TranslatedText.English("Barber"), TenantId = tenantId };
            context.Add(owner);
            await context.SaveChangesAsync(ct);
            user = owner.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, shop.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var db = Context(tenant);
        var visibility = new Visibility(db, request);

        (await visibility.CompanyAsync(ct)).Should().Be(shop.Corporate, "a barber shop that is its own root is its own company");

        var category = (await db.Set<CostCenterCategory>().SingleAsync(c => c.Code == "COMPANIES", ct)).Id;
        // A type that passes the type rule (a child of the company's type) but is configured to keep its own books: the tree rule refuses it.
        var branch = new CostCenterType { Code = "BRANCH_COMPANY", Name = TranslatedText.English("Branch company"), IdSup = shop.CompanyType, IsElement = true, KeepsBooks = true,
            TenantId = tenantId };
        db.Add(branch);
        await db.SaveChangesAsync(ct);
        var costCenters = new CostCenters(db, request, visibility) { Check = CostCenterRules.CompanyLevel(db) };
        var nested = () => costCenters.SaveElementAsync(new ElementDto
        {
            Name = new() { ["en"] = "A company inside the barber shop" }, TypeId = branch.Id, IdSup = shop.Corporate, CategoryIds = [category],
        }, ct);
        (await nested.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.CompanyLevelError);
    }

    [Fact]
    public async Task A_company_from_the_catalogue_takes_its_activitys_profile_and_the_rows_of_the_types_it_drops_go_with_them()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var barber = new CompanySetup("ARTI", "Berberia Arti", "AL", Activity: "BARBER", FromCatalogue: true);
        await using var db = Context(new TenantContext());
        var shop = Organisation.CreateCompany(db, tenantId, barber);
        Organisation.SetUpCompany(db, shop, barber);
        await db.SaveChangesAsync(ct);

        var codes = await db.Set<DocumentType>().Where(t => t.TenantId == tenantId).Select(t => t.Code).Distinct().ToListAsync(ct);
        codes.Should().Contain(["FSHSH", "PSH", "MA", "FAA", "FA", "FKSHSH", "KMA"], "Beauty's types, their reversal types and the journals' sheets");
        codes.Should().NotContain(["FSHM", "FTM", "OSHM", "KOBM"], "goods sales, transfers, offers and contracts are not a barber's");
        var types = db.Set<DocumentType>().Where(t => t.TenantId == tenantId).Select(t => (Guid?)t.Id);
        var links = db.Set<InterDocumentTypeTransactionType>().Where(l => types.Contains(l.DocumentTypeId)).Select(l => (Guid?)l.Id);
        (await db.Set<Scenario>().CountAsync(s => s.TenantId == tenantId && !links.Contains(s.DocumentTypeTransactionTypeId), ct)).Should().Be(0,
            "no scenario is left for a type the profile dropped");
        (await db.Set<CostCenterType>().Where(t => t.TenantId == tenantId && t.Code != null).Select(t => t.Code).ToListAsync(ct)).Should()
            .NotContain(["MG", "ZR"], "no warehouse, no office");
    }

    [Fact]
    public async Task A_pack_loads_in_groups_and_posting_needs_the_chart_and_the_documents()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var noPosting = new CompanySetup("ARTI", "Berberia Arti", "AL", Groups: ["chart", "documents", "depreciation"]);
        await using var db = Context(new TenantContext());
        var shop = Organisation.CreateCompany(db, tenantId, noPosting);
        Organisation.SetUpCompany(db, shop, noPosting);
        await db.SaveChangesAsync(ct);

        (await db.Set<Scenario>().CountAsync(s => s.TenantId == tenantId, ct)).Should().Be(0, "posting was left out");
        (await db.Set<DocumentType>().CountAsync(t => t.TenantId == tenantId, ct)).Should().BePositive("the documents group was loaded");
        var postingAlone = () => Organisation.SetUpCompany(db, shop, noPosting with { Groups = ["posting"] });
        postingAlone.Should().Throw<ArgumentException>().WithMessage("*posting group needs the chart group*");
    }
}

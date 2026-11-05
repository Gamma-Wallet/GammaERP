using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Posting;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>
/// The reports of an Albanian company with a September sale, an October sale reversed in full and an October purchase: the trial balance
/// summed up the chart's tree and balanced, an account's ledger with its opening balance, the sales and purchase VAT books, the VAT return.
/// </summary>
public sealed class VatReturnFormTests : IAsyncLifetime
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
    public async Task Albanias_VAT_return_fills_its_boxes_from_the_packs_rules_and_a_reversal_counts_negative()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", [("cost_center_type:WAREHOUSE", "WH", "Magazina")]);
        OrganisationIds company;
        Guid user;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            db.Add(staff);
            await db.SaveChangesAsync(ct);
            var owner = new User { TypeId = staff.Id, Code = "OWNER", Name = TranslatedText.English("Owner"), TenantId = tenantId };
            db.Add(owner);
            await db.SaveChangesAsync(ct);
            user = owner.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var ltd = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id;
        var client = new Partner { TypeId = ltd, Code = "C1", Name = TranslatedText.English("Bar Lura"), TenantId = tenantId, OwnerId = company.Corporate };
        var supplier = new Partner { TypeId = ltd, Code = "S1", Name = TranslatedText.English("Furnizuesi"), TenantId = tenantId, OwnerId = company.Corporate };
        context.AddRange(client, supplier);
        await context.SaveChangesAsync(ct);

        var accounts = await context.Set<Chart>().ToDictionaryAsync(c => c.Code!, ct);
        var lineType = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT", ct)).Id;
        var vat = await context.Set<VatGroup>().ToDictionaryAsync(v => (int)v.Rate!.Value, v => v.Id, ct);
        var warehouse = await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH", ct);
        var visibility = new Visibility(context, request);
        var documents = new DocumentService(context, request, visibility, TimeProvider.System);
        var types = await context.Set<DocumentType>().ToListAsync(ct);
        async Task<DocumentDto> PostAsync(string code, (Guid Id, string Name) from, (Guid Id, string Name) to, string account, decimal price, int rate, int day)
        {
            var type = types.Single(t => t.Code == code);
            var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == type.Id).Select(l => l.CategoryId).FirstAsync(ct);
            var date = new DateOnly(2026, 10, day);
            var saved = await documents.SaveAsync(new DocumentDto
            {
                TypeId = type.Id, CategoryIds = [category], DocumentDate = date, PostingDate = date, FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name,
                Transactions = [new TransactionDto { TypeId = lineType, ElementId = accounts[account].Id, ElementTypeId = accounts[account].TypeId, ElementName = account, Price = price,
                    VatGroupId = vat[rate] }],
            }, ct);
            var posted = await documents.PostAsync(saved.Id!.Value, ct);
            context.ChangeTracker.Clear();
            return posted;
        }

        var (store, bar, seller) = ((warehouse.Id, "Magazina"), (client.Id, "Bar Lura"), (supplier.Id, "Furnizuesi"));
        await PostAsync("FSHM", store, bar, "705", 1000, 20, 5);
        await PostAsync("FSHM", store, bar, "705", 500, 6, 6);
        var reversed = await PostAsync("FSHM", store, bar, "705", 200, 20, 7);
        await documents.ReverseAsync(reversed.Id!.Value, null, new DateOnly(2026, 10, 8), ct);
        context.ChangeTracker.Clear();
        await PostAsync("FBM", seller, store, "6011", 500, 20, 9);

        var form = await new ReportService(context, visibility).VatReturnFormAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "sq", ct);
        form.Boxes.Should().HaveCount(44, "Albania's form: boxes 9 to 24 (sales) and 26 to 53 (purchases)");
        form.Boxes.Single(b => b.Code == "13").Name.Should().Be("Shitje me shkallë 20% – vlera e tatueshme");
        // The 200 sale and its reversal cancel out in boxes 13 and 14.
        form.Boxes.Where(b => b.Value != 0).Select(b => (b.Code, b.Value)).Should().Equal(
            ("13", 1000m), ("14", 200m), ("17", 500m), ("18", 30m), ("38", 500m), ("39", 100m));
        (form.OutputVat, form.InputVat, form.Owed).Should().Be((230m, 100m, 130m));
    }
}

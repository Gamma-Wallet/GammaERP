using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
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
/// The founder's rules on reversals (2026-10-30): a reversal type is marked as one and reverses exactly one type; a reversal is made only by
/// reversing a posted document of that type and always carries that document's id and number, next to its own number of its own series.
/// </summary>
public sealed class ReversalRuleTests : IAsyncLifetime
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
    public async Task A_reversal_is_made_only_by_reversing_its_document_and_carries_its_id_and_number_and_the_database_keeps_one_reversal_type_per_type()
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
        var client = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id, Code = "C1", Name = TranslatedText.English("Bar Lura"),
            TenantId = tenantId, OwnerId = company.Corporate };
        context.Add(client);
        await context.SaveChangesAsync(ct);
        var types = await context.Set<DocumentType>().Where(t => t.Active).ToListAsync(ct);
        var sale = types.Single(t => t.Code == "FSHM");
        var saleReversal = types.Single(t => t.Code == "FKSHM");
        (saleReversal.IsReversal, saleReversal.ReversedDocumentTypeId, sale.IsReversal).Should().Be((true, sale.Id, false), "the sales invoice reversal type names the sales invoice type");

        var sales = await context.Set<DocumentCategory>().SingleAsync(c => c.Code == "SALES", ct);
        var reversals = await context.Set<DocumentCategory>().SingleAsync(c => c.Code == "REVERSALS", ct);
        var income = await context.Set<Chart>().SingleAsync(c => c.Code == "705", ct);
        var line = new TransactionDto { TypeId = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT", ct)).Id, ElementId = income.Id,
            ElementTypeId = income.TypeId, ElementName = "Mallra", Price = 1000, VatGroupId = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id };
        var warehouse = await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH", ct);
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var date = new DateOnly(2026, 10, 30);

        // A reversal on its own, without the document it reverses: refused.
        var alone = async () => await documents.SaveAsync(new DocumentDto
        {
            TypeId = saleReversal.Id, CategoryIds = [reversals.Id], DocumentDate = date, PostingDate = date, FromId = client.Id, FromName = "Bar Lura",
            ToId = warehouse.Id, ToName = "Magazina", Transactions = [line],
        }, ct);
        (await alone.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.ReversalError).And.Contain("made only by reversing the document it reverses");

        var bill = await documents.SaveAsync(new DocumentDto
        {
            TypeId = sale.Id, CategoryIds = [sales.Id], DocumentDate = date, PostingDate = date, FromId = warehouse.Id, FromName = "Magazina",
            ToId = client.Id, ToName = "Bar Lura", Transactions = [line],
        }, ct);
        bill = await documents.PostAsync(bill.Id!.Value, ct);
        var reversal = await documents.ReverseAsync(bill.Id!.Value, null, date, ct);
        (reversal.TypeId, reversal.ReversedDocumentId, reversal.ReversedDocumentCode).Should().Be((saleReversal.Id, bill.Id, bill.Code),
            "the reversal carries the document it reverses: its id and its number");
        reversal.Code.Should().StartWith(saleReversal.Code, "the reversal has its own number, of its own series");
        (await documents.DocumentAsync(reversal.Id!.Value, ct))!.ReversedDocumentCode.Should().Be("FSHM0");
        var stored = await context.Set<Document>().AsNoTracking().SingleAsync(d => d.Id == reversal.Id, ct);
        (stored.ReversedDocumentId, stored.ReversedDocumentCode).Should().Be((bill.Id, "FSHM0"));

        // The database keeps the type rules whatever writes to it: one reversal type per type, and a reversal type always names its type.
        await using var asOwner = Context(new TenantContext());
        asOwner.Add(new DocumentType { Code = "FKSHM2", Name = TranslatedText.English("Second sales invoice reversal"), IsElement = true, IsReversal = true,
            ReversedDocumentTypeId = sale.Id, TenantId = tenantId, OwnerId = company.Corporate });
        var second = async () => await asOwner.SaveChangesAsync(ct);
        (await second.Should().ThrowAsync<DbUpdateException>()).Which.InnerException!.Message.Should().Contain("ux_document_type_reversed");
        asOwner.ChangeTracker.Clear();
        asOwner.Add(new DocumentType { Code = "FKX", Name = TranslatedText.English("A reversal of nothing"), IsElement = true, IsReversal = true, TenantId = tenantId,
            OwnerId = company.Corporate });
        var nothing = async () => await asOwner.SaveChangesAsync(ct);
        (await nothing.Should().ThrowAsync<DbUpdateException>()).Which.InnerException!.Message.Should().Contain("ck_document_type_reversal");
    }
}

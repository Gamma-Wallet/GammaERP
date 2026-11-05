using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Posting;
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

/// <summary>A journal sheet done again for another month (recurring journal sheets): the sheet, its journals and their lines, linked and posted.</summary>
public sealed class RecurringJournalTypesTests : IAsyncLifetime
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
    public async Task Each_recurring_sheet_has_its_journals_twins_with_their_posting_and_reversals_and_a_recurring_entry_repeats()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", null);
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
        var types = await context.Set<DocumentType>().ToListAsync(ct);
        // Albania has two "MP" (the cash payment and the journal payment): the one under a sheet is the journal.
        var byCode = types.GroupBy(t => t.Code!).ToDictionary(g => g.Key, g => g.OrderBy(t => t.IdSup is null).First());

        // Albania's codes: each journal's code with the recurring mark R, under FKR / FAR / FRR; a reversal in the K…J pattern.
        types.Where(t => t.IdSup == byCode["FKR"].Id).Select(t => t.Code).Should().BeEquivalentTo(["MPR", "MAR", "PPR", "PKR", "PPPR", "GJR"]);
        types.Where(t => t.IdSup == byCode["FAR"].Id).Select(t => t.Code).Should().Equal("FAAR");
        types.Where(t => t.IdSup == byCode["FRR"].Id).Select(t => t.Code).Should().Equal("FRVR");
        (byCode["KMPRJ"].IsReversal, byCode["KMPRJ"].ReversedDocumentTypeId).Should().Be((true, byCode["MPR"].Id));
        byCode["MPR"].Name.In("sq").Should().Be(byCode["MP"].Name.In("sq"), "a twin keeps its journal's name");

        // Each twin posts as its journal does: the same line types, each with as many scenarios.
        async Task<List<(Guid? Kind, int Scenarios)>> PostingAsync(string code) =>
            [.. (await (from l in context.Set<InterDocumentTypeTransactionType>() where l.DocumentTypeId == byCode[code].Id
                        select new { l.TransactionTypeId, Count = context.Set<Scenario>().Count(s => s.DocumentTypeTransactionTypeId == l.Id) }).ToListAsync(ct))
                .Select(x => (x.TransactionTypeId, x.Count)).OrderBy(x => x.TransactionTypeId)];
        foreach (var (twin, original) in new[] { ("MPR", "MP"), ("MAR", "MA"), ("GJR", "GJ"), ("FAAR", "FAA"), ("KMPRJ", "KMPJ") })
        {
            (await PostingAsync(twin)).Should().Equal(await PostingAsync(original), $"{twin} posts as {original}");
        }

        var recurring = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == byCode["GJR"].Id).Select(l => l.CategoryId).ToListAsync(ct);
        (await context.Set<DocumentCategory>().Where(c => recurring.Contains(c.Id)).Select(c => c.Code).ToListAsync(ct)).Should().Equal("RECURRING_JOURNAL");

        // A recurring rent entry on the recurring journal sheet, repeated for October and posted.
        var accounts = await context.Set<Chart>().ToDictionaryAsync(c => c.Code!, ct);
        var lineType = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT", ct)).Id;
        var vat0 = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 0, ct)).Id;
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var september = new DateOnly(2026, 9, 30);
        TransactionDto Line(string account, decimal debit, decimal credit) => new()
        {
            TypeId = lineType, ElementId = accounts[account].Id, ElementTypeId = accounts[account].TypeId, ElementName = account, Price = debit + credit, Debit = debit,
            Credit = credit, VatGroupId = vat0,
        };
        var sheet = await documents.SaveAsync(new DocumentDto { TypeId = byCode["FKR"].Id, CategoryIds = recurring, DocumentDate = september, PostingDate = september }, ct);
        var rent = await documents.SaveAsync(new DocumentDto
        {
            TypeId = byCode["GJR"].Id, IdSup = sheet.Id, CategoryIds = recurring, DocumentDate = september, PostingDate = september, FromId = company.Corporate, ToId = company.Corporate,
            Transactions = [Line("6132", 1000, 0), Line("401", 0, 1000)],
        }, ct);
        await documents.PostAsync(rent.Id!.Value, ct);
        await documents.PostAsync(sheet.Id!.Value, ct);
        var october = new DateOnly(2026, 10, 31);
        var again = (await documents.RepeatAsync(sheet.Id!.Value, october, post: true, ct))!;
        var journal = await context.Set<Document>().AsNoTracking().SingleAsync(d => d.IdSup == again.Id, ct);
        (journal.TypeId, journal.PostingDate, journal.Posted).Should().Be((byCode["GJR"].Id, october, true));
        (await context.Set<ChartValue>().Where(v => v.DocumentId == journal.Id).SumAsync(v => v.Debit ?? 0, ct)).Should().Be(1000);
    }
}

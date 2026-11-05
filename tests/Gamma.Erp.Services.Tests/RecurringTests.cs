using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
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

/// <summary>A journal sheet done again for another month (recurring journal sheets): the sheet, its journals and their lines, linked and posted.</summary>
public sealed class RecurringTests : IAsyncLifetime
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
    public async Task A_journal_sheet_is_repeated_for_another_month_with_its_journals_and_lines_linked_to_what_they_repeat()
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
        var entryType = types.Single(t => t.Code == "GJ");
        var sheetType = types.Single(t => t.Id == entryType.IdSup);
        var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == entryType.Id).Select(l => l.CategoryId).FirstAsync(ct);
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

        var sheet = await documents.SaveAsync(new DocumentDto { TypeId = sheetType.Id, CategoryIds = [category], DocumentDate = september, PostingDate = september }, ct);
        var rent = await documents.SaveAsync(new DocumentDto
        {
            TypeId = entryType.Id, IdSup = sheet.Id, CategoryIds = [category], DocumentDate = september, PostingDate = september, FromId = company.Corporate, ToId = company.Corporate,
            Transactions = [Line("6132", 1000, 0), Line("401", 0, 1000)],
        }, ct);
        await documents.PostAsync(rent.Id!.Value, ct);
        await documents.PostAsync(sheet.Id!.Value, ct);
        var septemberEntries = await context.Set<ChartValue>().Where(v => v.DocumentId == rent.Id).ToListAsync(ct);
        septemberEntries.Sum(e => e.Debit).Should().Be(1000);

        var october = new DateOnly(2026, 10, 31);
        var again = (await documents.RepeatAsync(sheet.Id!.Value, october, post: true, ct))!;
        (again.TypeId, again.PostingDate, again.Posted).Should().Be((sheetType.Id, october, true));
        again.CreatedFromIds.Should().Equal(sheet.Id!.Value);
        again.Code.Should().NotBe(sheet.Code, "a new document of its type, with its own number");
        var journal = await context.Set<Document>().AsNoTracking().SingleAsync(d => d.IdSup == again.Id, ct);
        (journal.TypeId, journal.PostingDate, journal.Posted).Should().Be((entryType.Id, october, true));
        var octoberEntries = await context.Set<ChartValue>().Where(v => v.DocumentId == journal.Id).ToListAsync(ct);
        octoberEntries.Select(e => (e.ChartId, e.Debit, e.Credit, e.PostingDate)).Should().BeEquivalentTo(septemberEntries.Select(e => (e.ChartId, e.Debit, e.Credit, (DateOnly?)october)),
            "the same accounts and amounts, in October");

        var draft = (await documents.RepeatAsync(sheet.Id!.Value, new DateOnly(2026, 11, 30), post: false, ct))!;
        draft.Posted.Should().BeFalse();
        (await context.Set<ChartValue>().CountAsync(v => v.PostingDate == new DateOnly(2026, 11, 30), ct)).Should().Be(0, "a draft repeat posts nothing");
        (await documents.RepeatAsync(Guid.CreateVersion7(), october, post: false, ct)).Should().BeNull();
    }
}

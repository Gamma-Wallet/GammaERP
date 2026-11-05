using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>A sheet is a parent document whose rows are journals, each one document with one transaction; debit = credit holds over the sheet.</summary>
public sealed class SheetTests : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 10, 31);
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
    public async Task A_sheets_rows_are_journals_of_one_transaction_and_it_posts_whole_when_debit_equals_credit()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", [("cost_center_type:OFFICE", "HQ", "Zyra")]);
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
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var types = await context.Set<DocumentType>().ToListAsync(ct);
        var entry = types.Single(t => t.Code == "GJ");
        var sheetType = types.Single(t => t.Id == entry.IdSup);
        entry.IsElement.Should().BeTrue("the general journal");
        var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == sheetType.Id).Select(l => l.CategoryId).FirstAsync(ct);
        var accounts = await context.Set<Chart>().ToDictionaryAsync(c => c.Code!, ct);
        var lineType = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT", ct)).Id;
        var vat0 = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 0, ct)).Id;
        var office = (await context.Set<CostCenter>().SingleAsync(c => c.Code == "HQ", ct)).Id;
        DocumentDto Row(string account, decimal debit, decimal credit, Guid? id = null, Guid? type = null) => new()
        {
            Id = id, TypeId = type ?? entry.Id, FromId = office, ToId = office,
            Transactions = [new TransactionDto { TypeId = lineType, ElementId = accounts[account].Id, ElementTypeId = accounts[account].TypeId, ElementName = account,
                Price = debit + credit, Debit = debit, Credit = credit, VatGroupId = vat0 }],
        };
        var header = new DocumentDto { TypeId = sheetType.Id, CategoryIds = [category], DocumentDate = Day, PostingDate = Day };

        // Each row its own document under the sheet, with one transaction.
        var saved = await documents.SaveSheetAsync(new SheetDto(header, [Row("6132", 1000, 0), Row("401", 0, 900)]), ct);
        saved.Sheet.Transactions.Should().BeEmpty();
        saved.Journals.Should().HaveCount(2);
        saved.Journals.Should().AllSatisfy(j => { j.IdSup.Should().Be(saved.Sheet.Id); j.Transactions.Should().ContainSingle(); j.PostingDate.Should().Be(Day); });

        // Debit ≠ credit over the sheet: nothing is posted.
        var sheetId = saved.Sheet.Id!.Value;
        var unbalanced = async () => await documents.PostAsync(sheetId, ct);
        (await unbalanced.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("entries");
        context.ChangeTracker.Clear();
        (await context.Set<Document>().CountAsync(d => (d.Id == sheetId || d.IdSup == sheetId) && d.Posted, ct)).Should().Be(0);
        (await context.Set<ChartValue>().CountAsync(ct)).Should().Be(0);

        // Rows change: one changed, one taken out, two new. Each row balances only with the others.
        var changed = await documents.SaveSheetAsync(new SheetDto(saved.Sheet, [Row("6132", 700, 0, saved.Journals[0].Id), Row("6132", 300, 0), Row("401", 0, 1000)]), ct);
        changed.Journals.Should().HaveCount(3);
        (await documents.PostingPreviewAsync(sheetId, ct))!.Sum(r => r.Debit).Should().Be(1000);

        var posted = await documents.PostAsync(sheetId, ct);
        posted.Posted.Should().BeTrue();
        (await context.Set<Document>().CountAsync(d => d.IdSup == sheetId && d.Posted, ct)).Should().Be(3);
        var values = await context.Set<ChartValue>().ToListAsync(ct);
        (values.Sum(v => v.Debit), values.Sum(v => v.Credit)).Should().Be((1000m, 1000m));

        // Reversed as a whole: a reversal sheet, each journal's reversal under it, the books back to nothing; only once.
        var day = Day.AddDays(1);
        var reversal = await documents.ReverseAsync(sheetId, null, day, ct);
        (reversal.Posted, reversal.ReversedDocumentId).Should().Be((true, sheetId));
        (await documents.JournalsAsync(reversal.Id!.Value, ct)).Should().HaveCount(3).And.OnlyContain(j => j.Posted && j.Transactions.Count == 1);
        var after = await context.Set<ChartValue>().ToListAsync(ct);
        (after.Sum(v => v.Debit), after.Sum(v => v.Credit)).Should().Be((2000m, 2000m));
        after.GroupBy(v => v.ChartId).Should().OnlyContain(g => g.Sum(v => v.Debit) == g.Sum(v => v.Credit));
        var twice = async () => await documents.ReverseAsync(sheetId, null, day, ct);
        await twice.Should().ThrowAsync<ValidationException>();
        context.ChangeTracker.Clear();

        // Done again: one-sided rows post with their sheet.
        var again = (await documents.RepeatAsync(sheetId, Day.AddDays(1), post: true, ct))!;
        again.Posted.Should().BeTrue();
        (await context.Set<ChartValue>().ToListAsync(ct)).Sum(v => v.Debit).Should().Be(3000);

        // A row on a supplier or a cash desk moves its balance, and the reversal moves it back.
        var supplierType = await context.Set<TransactionType>().SingleAsync(t => t.Code == "SUPPLIER", ct);
        var partnerType = await context.Set<Gamma.Erp.Models.Partners.PartnerType>().SingleAsync(t => t.Code == "SME", ct);
        var seller = new Gamma.Erp.Models.Partners.Partner { TypeId = partnerType.Id, Code = "S1", Name = TranslatedText.English("Furnitori"), TenantId = tenantId, OwnerId = company.Corporate };
        context.AddRange(seller, new Gamma.Erp.Models.Partners.InterPartnerCategory { ElementId = seller.Id, CategoryId = (await context.Set<Gamma.Erp.Models.Partners.PartnerCategory>().SingleAsync(c => c.Code == "SUPPLIERS", ct)).Id, TenantId = tenantId, OwnerId = company.Corporate });
        await context.SaveChangesAsync(ct);
        var paid = await documents.SaveSheetAsync(new SheetDto(header, [
            Row("6132", 0, 250) , Row("6132", 250, 0) with { Transactions = [Row("6132", 250, 0).Transactions[0] with { TypeId = supplierType.Id, ElementId = seller.Id, ElementName = "Furnitori", ElementTypeId = partnerType.Id }] }]), ct);
        await documents.PostAsync(paid.Sheet.Id!.Value, ct);
        var balances = await context.Set<Gamma.Erp.Models.Partners.PartnerBalance>().Where(b => b.PartnerId == seller.Id).ToListAsync(ct);
        (balances.Sum(b => b.Debit ?? 0), balances.Sum(b => b.Credit ?? 0)).Should().Be((250m, 0m));
        await documents.ReverseAsync(paid.Sheet.Id!.Value, null, Day.AddDays(2), ct);
        balances = await context.Set<Gamma.Erp.Models.Partners.PartnerBalance>().Where(b => b.PartnerId == seller.Id).ToListAsync(ct);
        (balances.Sum(b => b.Debit ?? 0), balances.Sum(b => b.Credit ?? 0)).Should().Be((250m, 250m));

        // What a sheet refuses, with the row named.
        var twoLines = async () => await documents.SaveSheetAsync(new SheetDto(header, [Row("6132", 1, 0) with { Transactions = [.. Row("6132", 1, 0).Transactions, .. Row("401", 0, 1).Transactions] }]), ct);
        (await twoLines.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("journals[0].transactions");
        var other = types.First(t => t.IdSup is not null && t.IdSup != sheetType.Id);
        var wrongType = async () => await documents.SaveSheetAsync(new SheetDto(header, [Row("6132", 1, 0, null, other.Id)]), ct);
        (await wrongType.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("journals[0].typeId");
        var noAccount = async () => await documents.SaveSheetAsync(new SheetDto(header, [Row("6132", 1, 0) with { Transactions = [Row("6132", 1, 0).Transactions[0] with { ElementId = null, ElementName = null }] }]), ct);
        (await noAccount.Should().ThrowAsync<ValidationException>()).Which.Errors.Keys.Should().Contain(k => k.StartsWith("journals[0].transactions[0]", StringComparison.Ordinal));
        context.ChangeTracker.Clear();
        var empty = (await documents.SaveSheetAsync(new SheetDto(header, []), ct)).Sheet;
        var nothing = async () => await documents.PostAsync(empty.Id!.Value, ct);
        (await nothing.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("journals");
    }
}

using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Reports;

/// <summary>One account of a trial balance: its own entries and those of every account under it.</summary>
public sealed record TrialBalanceRowDto(Guid AccountId, string? Code, string? Name, int Level, bool IsGroup, decimal OpeningDebit, decimal OpeningCredit,
    decimal Debit, decimal Credit, decimal ClosingDebit, decimal ClosingCredit, decimal YearDebit = 0, decimal YearCredit = 0);

/// <summary>A trial balance; <c>YearDebit</c> / <c>YearCredit</c> are cumulative from <see cref="FiscalYearStart"/> to the period's end.</summary>
public sealed record TrialBalanceDto(Guid ChartCategoryId, string? Chart, DateOnly From, DateOnly To, IReadOnlyList<TrialBalanceRowDto> Rows, decimal TotalDebit,
    decimal TotalCredit, DateOnly FiscalYearStart = default);

public sealed record LedgerLineDto(DateOnly? PostingDate, Guid? DocumentId, string? Document, string? Account, string? From, string? To, decimal Debit, decimal Credit,
    decimal Balance);

public sealed record AccountLedgerDto(Guid AccountId, string? Code, string? Name, DateOnly From, DateOnly To, decimal Opening, IReadOnlyList<LedgerLineDto> Lines,
    decimal Closing);

/// <summary>The base and the VAT of one VAT group (null: lines without one).</summary>
public sealed record VatAmountDto(Guid? VatGroupId, string? VatGroup, decimal? Rate, decimal Base, decimal Vat);

/// <summary>One document of a VAT register; a reversal counts negative and names the document it reverses.</summary>
public sealed record VatRegisterLineDto(DateOnly? PostingDate, Guid DocumentId, string? Document, string? DocumentType, string? Partner, bool Reversal,
    IReadOnlyList<VatAmountDto> Amounts, decimal Base, decimal Vat, string? ReversedDocument = null,
    string? ExternalNumber = null, string? CostCenter = null, string? TaxNumber = null, string? City = null);

public sealed record VatRegisterDto(IReadOnlyList<Guid> CategoryIds, DateOnly From, DateOnly To, IReadOnlyList<VatRegisterLineDto> Lines, IReadOnlyList<VatAmountDto> Totals,
    decimal Base, decimal Vat);

/// <summary>A side's balance in one currency (a cost centre's, as a partner's): in that currency and in the company's.</summary>
public sealed record SideBalanceDto(Guid? CurrencyId, decimal Debit, decimal Credit, decimal DebitLcu, decimal CreditLcu)
{
    public decimal Balance => DebitLcu - CreditLcu;
}

/// <summary>One row of a side's ledger: the document (none for the balance brought forward), debit, credit and the running balance.</summary>
public sealed record SideLedgerRowDto(Guid? DocumentId, string? Document, DateOnly? PostingDate, decimal Debit, decimal Credit, decimal Balance);

/// <summary>The VAT of a period per VAT group: on sales (output) and on purchases (input), and what is owed (negative: to be refunded).</summary>
/// <summary>A box of the country's VAT return form and what the period puts in it.</summary>
public sealed record VatReturnBoxDto(Guid Id, string? Code, string? Name, bool IsInput, int Amount, decimal Value);

/// <summary>
/// The country's VAT return for a period, box by box, with the output VAT (sales boxes holding VAT), the input VAT (purchase boxes holding VAT)
/// and what is owed (negative: to be carried forward or refunded).
/// </summary>
public sealed record VatReturnFormDto(DateOnly From, DateOnly To, IReadOnlyList<VatReturnBoxDto> Boxes, decimal OutputVat, decimal InputVat, decimal Owed);

public sealed record VatReturnDto(DateOnly From, DateOnly To, IReadOnlyList<VatAmountDto> Output, IReadOnlyList<VatAmountDto> Input, decimal OutputVat, decimal InputVat,
    decimal Payable);

/// <summary>
/// The accounting reports, read from the posted entries and documents of the companies the request sees: the trial balance from the entries themselves, every period by its dates,
/// every level of the chart summed from all the accounts below it, no document types or accounts named in code). Which documents make
/// the sales or purchase VAT books is the caller's choice of document categories; a document of a type that is another type's reversal
/// type counts negative. Amounts in the company's currency.
/// </summary>
public sealed partial class ReportService(ErpDbContext db, Visibility visibility)
{
    /// <summary>The partner properties the VAT books show, by their English name in the pack (the founder, 2026-11-01); a company that renames one sees it empty.</summary>
    public const string TaxNumberProperty = "TAX_NUMBER";

    public const string CityProperty = "CITY";

    /// <summary>
    /// The trial balance of a chart (by default the company's statutory chart) for a period: per account, the balance before the period, the
    /// period's debit and credit, and the balance at its end; accounts with nothing on them are left out, the groups above those kept.
    /// </summary>
    public async Task<TrialBalanceDto> TrialBalanceAsync(DateOnly from, DateOnly to, Guid? chartCategoryId, string language, Guid? costCenter, CancellationToken ct)
    {
        Period(from, to);
        var category = await ChartCategoryAsync(chartCategoryId, ct).ConfigureAwait(false);
        var accounts = await (from c in db.Set<Chart>().AsNoTracking()
                              join l in db.Set<InterChartCategory>().AsNoTracking() on c.Id equals l.ElementId
                              where l.CategoryId == category.Id && l.Active && c.Active
                              select c).ToListAsync(ct).ConfigureAwait(false);
        var ids = accounts.Select(a => a.Id).ToList();
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        // What was posted before a change of the company's currency is read at its rate (one period, factor 1, when there is none).
        var periods = await Gamma.Erp.Services.Currencies.CurrencyHistory.PeriodsAsync(db, companies, to, ct).ConfigureAwait(false);
        var yearStart = await FiscalYearStartAsync(to, ct).ConfigureAwait(false);
        var sums = new List<(Guid? ChartId, bool Before, decimal Debit, decimal Credit)>();
        var year = new List<(Guid? ChartId, decimal Debit, decimal Credit)>();
        foreach (var period in periods)
        {
            var (since, until, factor) = (period.From, period.Until, period.Factor);
            var inPeriod = db.Set<ChartValue>().AsNoTracking().Where(v => v.Active && v.ChartId != null && ids.Contains(v.ChartId.Value) && companies.Contains(v.CompanyId)
                && v.PostingDate <= to && (costCenter == null || v.CostCenterId == costCenter) && (since == null || v.PostingDate >= since) && (until == null || v.PostingDate < until));
            sums.AddRange((await inPeriod.GroupBy(v => new { v.ChartId, Before = v.PostingDate < from })
                    .Select(g => new { g.Key.ChartId, g.Key.Before, Debit = g.Sum(v => v.Debit ?? 0), Credit = g.Sum(v => v.Credit ?? 0) }).ToListAsync(ct).ConfigureAwait(false))
                .Select(x => (x.ChartId, x.Before, Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(x.Debit, factor), Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(x.Credit, factor))));
            year.AddRange((await inPeriod.Where(v => v.PostingDate >= yearStart).GroupBy(v => v.ChartId)
                    .Select(g => new { ChartId = g.Key, Debit = g.Sum(v => v.Debit ?? 0), Credit = g.Sum(v => v.Credit ?? 0) }).ToListAsync(ct).ConfigureAwait(false))
                .Select(x => (x.ChartId, Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(x.Debit, factor), Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(x.Credit, factor))));
        }

        var byId = accounts.ToDictionary(a => a.Id);
        var totals = accounts.ToDictionary(a => a.Id, _ => new decimal[6]); // opening debit, opening credit, debit, credit, year debit, year credit
        foreach (var sum in sums)
        {
            // Every account's entries count on it and on every account above it.
            for (Guid? id = sum.ChartId; id is { } account && byId.TryGetValue(account, out var row); id = row.IdSup)
            {
                var t = totals[account];
                (t[sum.Before ? 0 : 2], t[sum.Before ? 1 : 3]) = (t[sum.Before ? 0 : 2] + sum.Debit, t[sum.Before ? 1 : 3] + sum.Credit);
            }
        }

        foreach (var sum in year)
        {
            for (Guid? id = sum.ChartId; id is { } account && byId.TryGetValue(account, out var row); id = row.IdSup)
            {
                (totals[account][4], totals[account][5]) = (totals[account][4] + sum.Debit, totals[account][5] + sum.Credit);
            }
        }

        var groups = accounts.Where(a => a.IdSup is not null).Select(a => a.IdSup!.Value).ToHashSet();
        int Level(Chart a) => a.IdSup is { } up && byId.TryGetValue(up, out var parent) ? Level(parent) + 1 : 1;
        var rows = accounts.Where(a => totals[a.Id].Any(v => v != 0)).OrderBy(a => a.Code, StringComparer.Ordinal).Select(a =>
        {
            var t = totals[a.Id];
            var opening = t[0] - t[1];
            var closing = opening + t[2] - t[3];
            return new TrialBalanceRowDto(a.Id, a.Code, a.Name.In(language), Level(a), groups.Contains(a.Id), Math.Max(opening, 0), Math.Max(-opening, 0), t[2], t[3],
                Math.Max(closing, 0), Math.Max(-closing, 0), t[4], t[5]);
        }).ToList();
        var leaves = rows.Where(r => !r.IsGroup).ToList();
        return new TrialBalanceDto(category.Id, category.Name.In(language), from, to, rows, leaves.Sum(r => r.Debit), leaves.Sum(r => r.Credit), yearStart);
    }

    /// <summary>The entries of an account (and of every account under it) in a period, with the balance before it and after each entry.</summary>
    public async Task<AccountLedgerDto?> LedgerAsync(Guid accountId, DateOnly from, DateOnly to, string language, Guid? costCenter, CancellationToken ct)
    {
        Period(from, to);
        if (await db.Set<Chart>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == accountId && c.Active, ct).ConfigureAwait(false) is not { } account)
        {
            return null;
        }

        var tree = await db.Set<Chart>().AsNoTracking().Where(c => c.OwnerId == account.OwnerId && c.Active).Select(c => new { c.Id, c.IdSup, c.Code })
            .ToListAsync(ct).ConfigureAwait(false);
        var below = tree.ToLookup(c => c.IdSup);
        var ids = new HashSet<Guid> { accountId };
        for (var queue = new Queue<Guid>([accountId]); queue.TryDequeue(out var id);)
        {
            foreach (var child in below[id].Where(c => ids.Add(c.Id)))
            {
                queue.Enqueue(child.Id);
            }
        }

        var codes = tree.ToDictionary(c => c.Id, c => c.Code);
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var values = db.Set<ChartValue>().AsNoTracking().Where(v => v.Active && v.ChartId != null && ids.Contains(v.ChartId.Value) && companies.Contains(v.CompanyId)
            && (costCenter == null || v.CostCenterId == costCenter));
        var periods = await Gamma.Erp.Services.Currencies.CurrencyHistory.PeriodsAsync(db, companies, to, ct).ConfigureAwait(false);
        var opening = 0m;
        foreach (var period in periods)
        {
            var (since, until) = (period.From, period.Until);
            opening += Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(await values.Where(v => v.PostingDate < from && (since == null || v.PostingDate >= since) && (until == null || v.PostingDate < until))
                .SumAsync(v => (v.Debit ?? 0) - (v.Credit ?? 0), ct).ConfigureAwait(false), period.Factor);
        }
        var inPeriod = values.Where(v => v.PostingDate >= from && v.PostingDate <= to);
        var lines = await (from v in inPeriod
                           join d in db.Set<Document>().AsNoTracking() on v.DocumentId equals d.Id into docs
                           from d in docs.DefaultIfEmpty()
                           orderby v.PostingDate, v.CreatedOn, v.Id
                           select new { v.PostingDate, v.DocumentId, d.Code, d.FromName, d.ToName, v.ChartId, Debit = v.Debit ?? 0, Credit = v.Credit ?? 0 })
            .ToListAsync(ct).ConfigureAwait(false);
        var balance = opening;
        var rows = lines.Select(l => (Line: l, Factor: Gamma.Erp.Services.Currencies.CurrencyHistory.FactorOn(periods, l.PostingDate))).Select(x => new LedgerLineDto(x.Line.PostingDate, x.Line.DocumentId, x.Line.Code,
            codes.GetValueOrDefault(x.Line.ChartId!.Value), x.Line.FromName, x.Line.ToName, Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(x.Line.Debit, x.Factor), Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(x.Line.Credit, x.Factor),
            balance += Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(x.Line.Debit, x.Factor) - Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(x.Line.Credit, x.Factor))).ToList();
        return new AccountLedgerDto(account.Id, account.Code, account.Name.In(language), from, to, opening, rows, balance);
    }

    /// <summary>
    /// The VAT register (sales or purchase book) of the documents of the given categories posted in a period, and the reversals of their
    /// types: per document its partner and its base and VAT per VAT group.
    /// </summary>
    public async Task<VatRegisterDto> VatRegisterAsync(IReadOnlyList<Guid> categoryIds, DateOnly from, DateOnly to, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(categoryIds);
        Period(from, to);
        if (categoryIds.Count == 0)
        {
            throw ValidationException.For("category", "Name at least one document category.");
        }

        var types = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => categoryIds.Contains(l.CategoryId) && l.Active).Select(l => l.TypeId)
            .Distinct().ToListAsync(ct).ConfigureAwait(false);
        var allTypes = await db.Set<DocumentType>().AsNoTracking().Where(t => t.Active).ToListAsync(ct).ConfigureAwait(false);
        var reversalTypes = allTypes.Where(t => t.IsReversal).Select(t => t.Id).ToHashSet();
        var included = types.Concat(allTypes.Where(t => t.IsReversal && t.ReversedDocumentTypeId is { } r && types.Contains(r)).Select(t => t.Id)).ToHashSet();
        var typeNames = allTypes.ToDictionary(t => t.Id, t => t.Name.In(language));

        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var documents = await db.Set<Document>().AsNoTracking()
            .Where(d => d.Active && d.Posted && included.Contains(d.TypeId) && companies.Contains(d.CompanyId) && d.PostingDate >= from && d.PostingDate <= to)
            .OrderBy(d => d.PostingDate).ThenBy(d => d.Code).ToListAsync(ct).ConfigureAwait(false);
        var documentIds = documents.Select(d => d.Id).ToList();
        var lines = await db.Set<Transaction>().AsNoTracking().Where(t => t.Active && t.DocumentId != null && documentIds.Contains(t.DocumentId.Value))
            .ToListAsync(ct).ConfigureAwait(false);
        var groupIds = lines.Where(l => l.VatGroupId is not null).Select(l => l.VatGroupId!.Value).Distinct().ToList();
        var groups = await db.Set<VatGroup>().AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct).ConfigureAwait(false);
        var sides = documents.SelectMany(d => new[] { d.FromId, d.ToId }).OfType<Guid>().Distinct().ToList();
        var partners = (await db.Set<Partner>().AsNoTracking().Where(p => sides.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var centers = (await db.Set<CostCenter>().AsNoTracking().Where(c => sides.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var details = await PartnerDetailsAsync(partners, language, ct).ConfigureAwait(false);

        var register = documents.Select(d =>
        {
            var sign = reversalTypes.Contains(d.TypeId) ? -1 : 1;
            var amounts = lines.Where(l => l.DocumentId == d.Id).GroupBy(l => l.VatGroupId)
                .Select(g => Amount(g.Key, groups, language, sign * g.Sum(l => l.ValueLcu ?? l.Value ?? 0), sign * g.Sum(l => l.VatLcu ?? l.Vat ?? 0)))
                .OrderBy(a => a.Rate).ToList();
            var partner = d.ToId is { } to && partners.Contains(to) ? d.ToName : d.FromId is { } f && partners.Contains(f) ? d.FromName : d.ToName;
            var partnerId = d.ToId is { } toPartner && partners.Contains(toPartner) ? toPartner : d.FromId is { } fromPartner && partners.Contains(fromPartner) ? fromPartner : (Guid?)null;
            var center = d.FromId is { } fromCenter && centers.Contains(fromCenter) ? d.FromName : d.ToId is { } toCenter && centers.Contains(toCenter) ? d.ToName : null;
            return new VatRegisterLineDto(d.PostingDate, d.Id, d.Code, typeNames.GetValueOrDefault(d.TypeId), partner, sign < 0, amounts, amounts.Sum(a => a.Base), amounts.Sum(a => a.Vat),
                d.ReversedDocumentCode, d.ExternalNumber, center, Detail(partnerId, TaxNumberProperty), Detail(partnerId, CityProperty));

            string? Detail(Guid? id, string property) => id is { } key ? details.GetValueOrDefault((key, property)) : null;
        }).ToList();
        var totals = register.SelectMany(r => r.Amounts).GroupBy(a => a.VatGroupId)
            .Select(g => g.First() with { Base = g.Sum(a => a.Base), Vat = g.Sum(a => a.Vat) }).OrderBy(a => a.Rate).ToList();
        return new VatRegisterDto(categoryIds, from, to, register, totals, totals.Sum(t => t.Base), totals.Sum(t => t.Vat));
    }

    /// <summary>The text properties of these partners by partner and English property name; a property left empty is not there.</summary>
    private async Task<Dictionary<(Guid Partner, string Property), string>> PartnerDetailsAsync(IReadOnlyCollection<Guid> partners, string language, CancellationToken ct)
    {
        var values = await (from v in db.Set<PartnerInfo>().AsNoTracking() join t in db.Set<PartnerTypeInfo>().AsNoTracking() on v.TypeInfoId equals t.Id
                            where v.Active && partners.Contains(v.ElementId) && t.Kind == PropertyKind.Text && t.Code != null select new { v.ElementId, t.Code, v.Value }).ToListAsync(ct).ConfigureAwait(false);
        return values.Select(v => (Key: (v.ElementId, v.Code!), Text: PropertyText(v.Value, language)))
            .Where(v => !string.IsNullOrEmpty(v.Text)).GroupBy(v => v.Key).ToDictionary(g => g.Key, g => g.First().Text!);
    }

    private static string? PropertyText(JsonNode? value, string language) =>
        (value is JsonObject texts ? texts[language] ?? texts.Select(t => t.Value).FirstOrDefault(t => t is not null) : value)?.ToString().Trim();

    /// <summary>
    /// The country's VAT return form for a period (H13, the founder: per country), from the company's boxes and their rules (configuration,
    /// tbl_vat_return_box and _rule): each posted line with a VAT group goes, for its taxable value and for its VAT, into the box of the most
    /// specific rule that takes it on each side (a document type counts 4, a line type 2, a category and a VAT group 1 each); a reversal
    /// counts negative. Nothing about a country is in the code.
    /// </summary>
    public async Task<VatReturnFormDto> VatReturnFormAsync(DateOnly from, DateOnly to, string language, CancellationToken ct)
    {
        Period(from, to);
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var boxes = await db.Set<VatReturnBox>().AsNoTracking().Where(b => b.Active && b.OwnerId != null && companies.Contains(b.OwnerId.Value))
            .OrderBy(b => b.SortOrder).ToListAsync(ct).ConfigureAwait(false);
        var boxIds = boxes.Select(b => b.Id).ToList();
        var rules = await db.Set<VatReturnBoxRule>().AsNoTracking().Where(r => r.Active && boxIds.Contains(r.BoxId)).ToListAsync(ct).ConfigureAwait(false);
        var allTypes = await db.Set<DocumentType>().AsNoTracking().Where(t => t.Active).ToListAsync(ct).ConfigureAwait(false);
        var original = allTypes.ToDictionary(t => t.Id, t => t.IsReversal && t.ReversedDocumentTypeId is { } r ? r : t.Id);
        var categories = (await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.Active).ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(l => l.TypeId, l => l.CategoryId);

        var documents = await db.Set<Document>().AsNoTracking()
            .Where(d => d.Active && d.Posted && !db.Set<DocumentType>().Where(k => k.NeverPosted).Select(k => k.Id).Contains(d.TypeId) && companies.Contains(d.CompanyId) && d.PostingDate >= from && d.PostingDate <= to)
            .ToListAsync(ct).ConfigureAwait(false);
        var byId = documents.ToDictionary(d => d.Id);
        var documentIds = byId.Keys.ToList();
        var lines = await db.Set<Transaction>().AsNoTracking().Where(t => t.Active && t.VatGroupId != null && t.DocumentId != null && documentIds.Contains(t.DocumentId.Value))
            .ToListAsync(ct).ConfigureAwait(false);
        var kinds = await db.Set<TransactionTypeConf>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.TransactionTypeId, ct).ConfigureAwait(false);
        var boxOf = boxes.ToDictionary(b => b.Id);

        var totals = boxes.ToDictionary(b => b.Id, _ => 0m);
        foreach (var line in lines)
        {
            var document = byId[line.DocumentId!.Value];
            var type = original.GetValueOrDefault(document.TypeId, document.TypeId);
            var sign = type == document.TypeId ? 1 : -1;
            var kind = line.TypeConfId is { } c ? kinds.GetValueOrDefault(c) : null;
            var taken = rules.Where(r => (r.DocumentTypeId == null || r.DocumentTypeId == type) && (r.TransactionTypeId == null || r.TransactionTypeId == kind)
                                         && (r.VatGroupId == null || r.VatGroupId == line.VatGroupId) && (r.DocumentCategoryId == null || categories[type].Contains(r.DocumentCategoryId.Value)))
                .Select(r => (Rule: r, Box: boxOf[r.BoxId]))
                .GroupBy(x => (x.Box.IsInput, x.Box.Amount))
                .Select(g => g.OrderByDescending(x => (x.Rule.DocumentTypeId is null ? 0 : 4) + (x.Rule.TransactionTypeId is null ? 0 : 2)
                                                      + (x.Rule.DocumentCategoryId is null ? 0 : 1) + (x.Rule.VatGroupId is null ? 0 : 1)).First().Box);
            foreach (var box in taken)
            {
                totals[box.Id] += sign * (box.Amount == 1 ? line.ValueLcu ?? line.Value ?? 0 : line.VatLcu ?? line.Vat ?? 0);
            }
        }

        var result = boxes.Select(b => new VatReturnBoxDto(b.Id, b.Code, b.Name?.In(language), b.IsInput, b.Amount, totals[b.Id])).ToList();
        var output = result.Where(b => !b.IsInput && b.Amount == 2).Sum(b => b.Value);
        var input = result.Where(b => b.IsInput && b.Amount == 2).Sum(b => b.Value);
        return new VatReturnFormDto(from, to, result, output, input, output - input);
    }

    /// <summary>The VAT of a period: the sales register's (output) against the purchase register's (input), per VAT group, and what is owed.</summary>
    public async Task<VatReturnDto> VatReturnAsync(IReadOnlyList<Guid> outputCategoryIds, IReadOnlyList<Guid> inputCategoryIds, DateOnly from, DateOnly to, string language,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outputCategoryIds);
        ArgumentNullException.ThrowIfNull(inputCategoryIds);
        if (outputCategoryIds.Count == 0 && inputCategoryIds.Count == 0)
        {
            throw ValidationException.For("category", "Name at least one document category.");
        }

        // A side with no categories (a business that buys nothing with VAT) counts as nothing.
        var output = outputCategoryIds.Count == 0 ? null : await VatRegisterAsync(outputCategoryIds, from, to, language, ct).ConfigureAwait(false);
        var input = inputCategoryIds.Count == 0 ? null : await VatRegisterAsync(inputCategoryIds, from, to, language, ct).ConfigureAwait(false);
        var (outputVat, inputVat) = (output?.Vat ?? 0, input?.Vat ?? 0);
        return new VatReturnDto(from, to, output?.Totals ?? [], input?.Totals ?? [], outputVat, inputVat, outputVat - inputVat);
    }

    /// <summary>
    /// A cost centre's balance up to a date, per currency, from its posted documents' balance
    /// rows (tbl_cost_center_balance); null when the request does not see the cost centre.
    /// </summary>
    public async Task<IReadOnlyList<SideBalanceDto>?> CostCenterBalanceAsync(Guid costCenterId, DateOnly? until, CancellationToken ct)
    {
        if (!await SeesCostCenterAsync(costCenterId, ct).ConfigureAwait(false))
        {
            return null;
        }

        var rows = await CostCenterRowsAsync(costCenterId, null, until, ct).ConfigureAwait(false);
        return [.. rows.GroupBy(r => r.Balance.CurrencyId).Select(g => new SideBalanceDto(g.Key, g.Sum(r => r.Balance.Debit ?? 0), g.Sum(r => r.Balance.Credit ?? 0),
            g.Sum(r => r.Balance.DebitLcu ?? 0), g.Sum(r => r.Balance.CreditLcu ?? 0)))];
    }

    /// <summary>
    /// The documents behind a cost centre's balance in a period: the balance before
    /// the period, then each posted document with its debit, credit (company currency) and the running balance; null when it is not seen.
    /// </summary>
    public async Task<IReadOnlyList<SideLedgerRowDto>?> CostCenterLedgerAsync(Guid costCenterId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        Period(from, to);
        if (!await SeesCostCenterAsync(costCenterId, ct).ConfigureAwait(false))
        {
            return null;
        }

        var before = await CostCenterRowsAsync(costCenterId, null, from.AddDays(-1), ct).ConfigureAwait(false);
        var rows = await CostCenterRowsAsync(costCenterId, from, to, ct).ConfigureAwait(false);
        var balance = before.Sum(r => (r.Balance.DebitLcu ?? 0) - (r.Balance.CreditLcu ?? 0));
        var ledger = new List<SideLedgerRowDto> { new(null, null, from, 0, 0, balance) };
        foreach (var d in rows.GroupBy(r => r.Document.Id).Select(g => (g.First().Document, Debit: g.Sum(r => r.Balance.DebitLcu ?? 0), Credit: g.Sum(r => r.Balance.CreditLcu ?? 0)))
            .OrderBy(d => d.Document.PostingDate).ThenBy(d => d.Document.Id))
        {
            balance += d.Debit - d.Credit;
            ledger.Add(new SideLedgerRowDto(d.Document.Id, d.Document.Code, d.Document.PostingDate, d.Debit, d.Credit, balance));
        }

        return ledger;
    }

    /// <summary>The company setting that says in which month its fiscal year starts (1 to 12; January when empty).</summary>
    public const string FiscalYearProperty = "Fiscal year starts (month)";

    /// <summary>The first day of the fiscal year a day falls in, by the request's company's setting.</summary>
    public async Task<DateOnly> FiscalYearStartAsync(DateOnly day, CancellationToken ct)
    {
        var month = 1;
        if (await visibility.CompanyAsync(ct).ConfigureAwait(false) is { } company)
        {
            var values = await (from v in db.Set<Models.CostCenters.CostCenterInfo>().AsNoTracking()
                                join p in db.Set<Models.CostCenters.CostCenterTypeInfo>().AsNoTracking() on v.TypeInfoId equals p.Id
                                where v.ElementId == company && v.Active && p.Kind == Models.Base.PropertyKind.Number
                                select new { p.Name, v.Value }).ToListAsync(ct).ConfigureAwait(false);
            if (values.FirstOrDefault(x => string.Equals(x.Name.In(Models.Base.TranslatedText.DefaultLanguage), FiscalYearProperty, StringComparison.OrdinalIgnoreCase))?.Value
                is { } value && (int)value.GetValue<decimal>() is >= 1 and <= 12 and var m)
            {
                month = m;
            }
        }

        var start = new DateOnly(day.Year, month, 1);
        return start > day ? start.AddYears(-1) : start;
    }

    private async Task<bool> SeesCostCenterAsync(Guid costCenterId, CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        return nodes.Contains(costCenterId)
            || await db.Set<Models.CostCenters.CostCenter>().AsNoTracking().AnyAsync(c => c.Id == costCenterId && c.Active && c.OwnerId != null && nodes.Contains(c.OwnerId.Value), ct)
                .ConfigureAwait(false);
    }

    private async Task<List<(Models.CostCenters.CostCenterBalance Balance, Document Document)>> CostCenterRowsAsync(Guid costCenterId, DateOnly? since, DateOnly? until,
        CancellationToken ct)
    {
        var rows = await (from b in db.Set<Models.CostCenters.CostCenterBalance>().AsNoTracking()
                          join d in db.Set<Document>().AsNoTracking() on b.DocumentId equals d.Id
                          where b.CostCenterId == costCenterId && b.Active && d.Active && d.Posted && (since == null || d.PostingDate >= since) && (until == null || d.PostingDate <= until)
                          select new { b, d }).ToListAsync(ct).ConfigureAwait(false);
        // Read in the currency of the last day asked: what was posted before a change of the company's currency, at its rate.
        var periods = await Gamma.Erp.Services.Currencies.CurrencyHistory.PeriodsAsync(db, [.. rows.Select(r => r.d.CompanyId).Distinct()], until ?? DateOnly.FromDateTime(DateTime.Today), ct).ConfigureAwait(false);
        foreach (var r in rows)
        {
            var factor = Gamma.Erp.Services.Currencies.CurrencyHistory.FactorOn(periods, r.d.PostingDate);
            (r.b.DebitLcu, r.b.CreditLcu) = (r.b.DebitLcu is { } debit ? Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(debit, factor) : null, r.b.CreditLcu is { } credit ? Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(credit, factor) : null);
        }

        return [.. rows.Select(x => (x.b, x.d))];
    }

    private static VatAmountDto Amount(Guid? group, Dictionary<Guid, VatGroup> groups, string language, decimal value, decimal vat) =>
        group is { } id && groups.TryGetValue(id, out var g) ? new(id, g.Name?.In(language), g.Rate, value, vat) : new(null, null, null, value, vat);

    private async Task<ChartCategory> ChartCategoryAsync(Guid? id, CancellationToken ct)
    {
        if (id is { } chosen)
        {
            return await db.Set<ChartCategory>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == chosen && c.Active, ct).ConfigureAwait(false)
                ?? throw ValidationException.For("chart", "There is no such chart.");
        }

        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("chart", "Name the chart: the request is not inside one company.");
        return await db.Set<ChartCategory>().AsNoTracking().FirstOrDefaultAsync(c => c.OwnerId == company && c.IsStatutory && c.Active, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("chart", "The company has no statutory chart.");
    }

    private static void Period(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw ValidationException.For("to", "The period ends before it starts.");
        }
    }
}

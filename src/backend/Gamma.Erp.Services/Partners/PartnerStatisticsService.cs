using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Partners.Dto;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Partners;

/// <summary>
/// CRM statistics per partner, from posted documents only and only those the request's node sees. Nothing is named in code: every figure is grouped by document
/// category as configured. The balance comes from tbl_partner_balance.
/// </summary>
public sealed class PartnerStatisticsService(ErpDbContext db, Visibility visibility)
{
    /// <summary>A partner's figures, optionally for a period of posting dates.</summary>
    public async Task<PartnerStatisticsDto?> StatisticsAsync(Guid partnerId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var partner = await db.Set<Partner>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == partnerId, ct).ConfigureAwait(false);
        if (partner is null)
        {
            return null;
        }

        var rows = await RowsAsync(partnerId, from, to, ct).ConfigureAwait(false);
        var documentIds = rows.Select(r => r.Document.Id).Distinct().ToList();
        var categories = await CategoriesAsync(documentIds, ct).ConfigureAwait(false);
        var lines = await db.Set<Transaction>().AsNoTracking().Where(t => t.DocumentId != null && documentIds.Contains(t.DocumentId.Value)).ToListAsync(ct).ConfigureAwait(false);
        // The lines in the same currency as the balances: each at its document's day's factor.
        var linePeriods = await Gamma.Erp.Services.Currencies.CurrencyHistory.PeriodsAsync(db, [.. rows.Select(r => r.Document.CompanyId).Distinct()], to ?? DateOnly.FromDateTime(DateTime.Today), ct).ConfigureAwait(false);
        var dayOfDocument = rows.GroupBy(r => r.Document.Id).ToDictionary(g => g.Key, g => g.First().Document.PostingDate);
        foreach (var l in lines)
        {
            var factor = Gamma.Erp.Services.Currencies.CurrencyHistory.FactorOn(linePeriods, dayOfDocument.GetValueOrDefault(l.DocumentId!.Value));
            (l.ValueLcu, l.VatLcu, l.ValueVatLcu) = (l.ValueLcu is { } v ? Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(v, factor) : null, l.VatLcu is { } t ? Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(t, factor) : null,
                l.ValueVatLcu is { } w ? Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(w, factor) : null);
        }
        var currencyIds = rows.Select(r => r.Balance.CurrencyId).Distinct().ToList();
        var currencies = await db.Set<Currency>().AsNoTracking().Where(c => currencyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);

        var perCurrency = rows.GroupBy(r => r.Balance.CurrencyId).Select(g => new PartnerCurrencyBalanceDto(g.Key,
            g.Key is { } c && currencies.TryGetValue(c, out var currency) ? currency.Name?.In(TranslatedText.DefaultLanguage) ?? currency.Code : null,
            g.Sum(r => r.Balance.Debit ?? 0), g.Sum(r => r.Balance.Credit ?? 0), g.Sum(r => r.Balance.DebitLcu ?? 0), g.Sum(r => r.Balance.CreditLcu ?? 0))).ToList();

        var perCategory = categories.SelectMany(c => c.Value.Select(cat => (Document: c.Key, cat.Id, cat.Name))).GroupBy(x => (x.Id, x.Name)).Select(g =>
        {
            var docs = g.Select(x => x.Document).ToHashSet();
            var docLines = lines.Where(l => docs.Contains(l.DocumentId!.Value)).ToList();
            var docRows = rows.Where(r => docs.Contains(r.Document.Id)).ToList();
            return new PartnerCategoryFiguresDto(g.Key.Id, g.Key.Name, docs.Count, docLines.Sum(l => l.ValueLcu ?? 0), docLines.Sum(l => l.VatLcu ?? 0),
                docLines.Sum(l => l.ValueVatLcu ?? 0), docRows.Sum(r => r.Balance.DebitLcu ?? 0), docRows.Sum(r => r.Balance.CreditLcu ?? 0));
        }).OrderBy(c => c.Category, StringComparer.OrdinalIgnoreCase).ToList();

        var dates = rows.Where(r => r.Document.PostingDate is not null).Select(r => r.Document.PostingDate!.Value).ToList();
        var dateOf = rows.GroupBy(r => r.Document.Id).ToDictionary(g => g.Key, g => g.First().Document.PostingDate);
        var months = categories.SelectMany(c => c.Value.Select(cat => (Document: c.Key, cat.Id, cat.Name)))
            .Where(x => dateOf.GetValueOrDefault(x.Document) is not null)
            .GroupBy(x => (Month: dateOf[x.Document]!.Value.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), x.Id, x.Name))
            .Select(g => new PartnerMonthDto(g.Key.Month, g.Key.Id, g.Key.Name, lines.Where(l => g.Select(x => x.Document).Contains(l.DocumentId!.Value)).Sum(l => l.ValueVatLcu ?? 0)))
            .OrderBy(m => m.Month, StringComparer.Ordinal).ThenBy(m => m.Category, StringComparer.OrdinalIgnoreCase).ToList();

        var productIds = lines.Where(l => l.ElementId is not null).Select(l => l.ElementId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        var top = lines.Where(l => l.ElementId is { } e && products.ContainsKey(e)).GroupBy(l => l.ElementId!.Value)
            .Select(g => new PartnerProductDto(g.Key, products[g.Key].Name.In(TranslatedText.DefaultLanguage), g.Sum(l => l.Quantity ?? 0), g.Sum(l => l.ValueLcu ?? 0)))
            .OrderByDescending(p => p.Value).Take(10).ToList();

        return new PartnerStatisticsDto(partner.Id, partner.Name.In(TranslatedText.DefaultLanguage), perCurrency.Sum(c => c.DebitLcu - c.CreditLcu), perCurrency,
            dates.Count == 0 ? null : dates.Min(), dates.Count == 0 ? null : dates.Max(), perCategory, months, top);
    }

    /// <summary>
    /// The partner's ledger for a period: the balance before it as the first row, then each posted document with its
    /// debit, credit (local currency) and the running balance, in posting order.
    /// </summary>
    public async Task<IReadOnlyList<PartnerLedgerRowDto>> LedgerAsync(Guid partnerId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var before = await RowsAsync(partnerId, null, from.AddDays(-1), ct).ConfigureAwait(false);
        var rows = await RowsAsync(partnerId, from, to, ct).ConfigureAwait(false);
        var categories = await CategoriesAsync(rows.Select(r => r.Document.Id).Distinct().ToList(), ct).ConfigureAwait(false);
        var balance = before.Sum(r => (r.Balance.DebitLcu ?? 0) - (r.Balance.CreditLcu ?? 0));
        var ledger = new List<PartnerLedgerRowDto> { new(null, null, from, null, 0, 0, balance) };
        foreach (var document in rows.GroupBy(r => r.Document.Id).Select(g => (g.First().Document, Debit: g.Sum(r => r.Balance.DebitLcu ?? 0), Credit: g.Sum(r => r.Balance.CreditLcu ?? 0)))
            .OrderBy(d => d.Document.PostingDate).ThenBy(d => d.Document.Id))
        {
            balance += document.Debit - document.Credit;
            ledger.Add(new PartnerLedgerRowDto(document.Document.Id, document.Document.Code, document.Document.PostingDate,
                string.Join(", ", categories.GetValueOrDefault(document.Document.Id, []).Select(c => c.Name)), document.Debit, document.Credit, balance));
        }

        return ledger;
    }

    /// <summary>
    /// All partners ranked for one document category and period: their posted documents of
    /// that category, debit and credit in the local currency; aggregated and paged in the database.
    /// </summary>
    public async Task<PageDto<PartnerRankDto>> RankingAsync(Guid categoryId, DateOnly? since, DateOnly? until, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = (Math.Max(page, 1), Math.Clamp(pageSize, 1, 500));
        var nodes = (await visibility.NodesAsync(ct).ConfigureAwait(false)).ToList();
        var inCategory = db.Set<InterDocumentCategory>().Where(l => l.CategoryId == categoryId).Select(l => l.ElementId);
        var periods = await Gamma.Erp.Services.Currencies.CurrencyHistory.PeriodsAsync(db, await visibility.CompaniesAsync(ct).ConfigureAwait(false), until ?? DateOnly.FromDateTime(DateTime.Today), ct)
            .ConfigureAwait(false);
        if (periods.Count > 1)
        {
            // The period spans a change of the company's currency: each row at its day's factor, ranked here rather than in the database.
            var all = await (from b in db.Set<PartnerBalance>().AsNoTracking()
                             join d in db.Set<Document>().AsNoTracking() on b.DocumentId equals d.Id
                             where d.Posted && d.OwnerId != null && nodes.Contains(d.OwnerId.Value) && inCategory.Contains(d.Id)
                                 && (since == null || d.PostingDate >= since) && (until == null || d.PostingDate <= until) && b.PartnerId != null
                             select new { Partner = b.PartnerId!.Value, Document = d.Id, d.PostingDate, Debit = b.DebitLcu ?? 0, Credit = b.CreditLcu ?? 0 })
                .ToListAsync(ct).ConfigureAwait(false);
            var ranked = all.GroupBy(x => x.Partner).Select(g => (Partner: g.Key, Documents: g.Select(x => x.Document).Distinct().Count(),
                    Debit: g.Sum(x => Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(x.Debit, Gamma.Erp.Services.Currencies.CurrencyHistory.FactorOn(periods, x.PostingDate))), Credit: g.Sum(x => Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(x.Credit, Gamma.Erp.Services.Currencies.CurrencyHistory.FactorOn(periods, x.PostingDate)))))
                .OrderByDescending(x => x.Debit + x.Credit).ThenBy(x => x.Partner).ToList();
            var shown = ranked.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            var shownIds = shown.Select(i => i.Partner).ToList();
            var shownNames = await db.Set<Partner>().AsNoTracking().Where(p => shownIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name.In(TranslatedText.DefaultLanguage), ct)
                .ConfigureAwait(false);
            return new PageDto<PartnerRankDto>([.. shown.Select(i => new PartnerRankDto(i.Partner, shownNames.GetValueOrDefault(i.Partner), i.Documents, i.Debit, i.Credit))],
                ranked.Count, page, pageSize);
        }

        var grouped = from b in db.Set<PartnerBalance>()
                      join d in db.Set<Document>() on b.DocumentId equals d.Id
                      where d.Posted && d.OwnerId != null && nodes.Contains(d.OwnerId.Value) && inCategory.Contains(d.Id)
                          && (since == null || d.PostingDate >= since) && (until == null || d.PostingDate <= until) && b.PartnerId != null
                      group new { b, d } by b.PartnerId into g
                      select new { Partner = g.Key!.Value, Documents = g.Select(x => x.d.Id).Distinct().Count(), Debit = g.Sum(x => x.b.DebitLcu ?? 0), Credit = g.Sum(x => x.b.CreditLcu ?? 0) };
        var total = await grouped.CountAsync(ct).ConfigureAwait(false);
        var items = await grouped.OrderByDescending(x => x.Debit + x.Credit).ThenBy(x => x.Partner).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        var ids = items.Select(i => i.Partner).ToList();
        var names = await db.Set<Partner>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name.In(TranslatedText.DefaultLanguage), ct).ConfigureAwait(false);
        return new PageDto<PartnerRankDto>([.. items.Select(i => new PartnerRankDto(i.Partner, names.GetValueOrDefault(i.Partner), i.Documents, i.Debit, i.Credit))], total, page, pageSize);
    }

    /// <summary>The partner's balance rows of posted documents the request sees, with their documents.</summary>
    private async Task<List<(PartnerBalance Balance, Document Document)>> RowsAsync(Guid partnerId, DateOnly? since, DateOnly? until, CancellationToken ct)
    {
        var nodes = (await visibility.NodesAsync(ct).ConfigureAwait(false)).ToList();
        var rows = await (from b in db.Set<PartnerBalance>().AsNoTracking()
                          join d in db.Set<Document>().AsNoTracking() on b.DocumentId equals d.Id
                          where b.PartnerId == partnerId && d.Posted && d.OwnerId != null && nodes.Contains(d.OwnerId.Value)
                              && (since == null || d.PostingDate >= since) && (until == null || d.PostingDate <= until)
                          select new { b, d }).ToListAsync(ct).ConfigureAwait(false);
        // In the currency of the last day asked: what was posted before a change of the company's currency, at its rate.
        var periods = await Gamma.Erp.Services.Currencies.CurrencyHistory.PeriodsAsync(db, [.. rows.Select(r => r.d.CompanyId).Distinct()], until ?? DateOnly.FromDateTime(DateTime.Today), ct).ConfigureAwait(false);
        foreach (var r in rows)
        {
            var factor = Gamma.Erp.Services.Currencies.CurrencyHistory.FactorOn(periods, r.d.PostingDate);
            (r.b.DebitLcu, r.b.CreditLcu) = (r.b.DebitLcu is { } debit ? Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(debit, factor) : null, r.b.CreditLcu is { } credit ? Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(credit, factor) : null);
        }

        return [.. rows.Select(r => (r.b, r.d))];
    }

    /// <summary>Each document's categories (id, name).</summary>
    private async Task<Dictionary<Guid, List<(Guid Id, string? Name)>>> CategoriesAsync(List<Guid> documentIds, CancellationToken ct)
    {
        var links = await (from l in db.Set<InterDocumentCategory>().AsNoTracking()
                           join c in db.Set<DocumentCategory>().AsNoTracking() on l.CategoryId equals c.Id
                           where documentIds.Contains(l.ElementId) select new { l.ElementId, c.Id, c.Name }).ToListAsync(ct).ConfigureAwait(false);
        return links.GroupBy(l => l.ElementId).ToDictionary(g => g.Key, g => g.Select(l => (l.Id, l.Name.In(TranslatedText.DefaultLanguage))).ToList());
    }
}

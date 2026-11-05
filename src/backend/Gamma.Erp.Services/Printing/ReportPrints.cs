using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Products;
using Microsoft.EntityFrameworkCore;
using static Gamma.Erp.Services.Printing.PdfLayout;

namespace Gamma.Erp.Services.Printing;

/// <summary>The reports printed since C3 phase 6 (B9): stock, item tracking, sales and purchase lines, partner and cost centre ledgers, partners.</summary>
public sealed partial class PrintService
{
    public async Task<PdfFile> StockAsync(Guid? costCenter, Guid? productCategory, string holder, string language, CancellationToken ct)
    {
        var rows = await stock.StockAsync(costCenter, productCategory, holder, language, ct).ConfigureAwait(false);
        var culture = Culture(language);
        var (pdf, section) = Page(await RequestCompanyAsync(language, ct).ConfigureAwait(false), Word(language, "stock"), null, language, landscape: true);
        var lines = rows.Select(r => ((IReadOnlyList<string>)[r.CostCenter ?? "", r.Code ?? "", r.Product ?? "", r.Unit ?? "", Quantity(r.Quantity, culture),
            Amount(r.UnitCost, culture), Amount(r.Value, culture)], false))
            .Append(([string.Empty, string.Empty, Word(language, "total"), "", "", "", Amount(rows.Sum(r => r.Value), culture)], true));
        Table(section, [(Word(language, "costCenter"), 4.6, false), (Word(language, "item"), 2.4, false), (Word(language, "name"), 7.4, false),
            (Word(language, "unit"), 2.2, false), (Word(language, "quantity"), 2.6, true), (Word(language, "unitCost"), 2.6, true), (Word(language, "value"), 3.0, true)], lines);
        return new PdfFile(Render(pdf), $"stock-{DateTime.UtcNow:yyyy-MM-dd}.pdf");
    }

    public async Task<PdfFile> ItemTrackingAsync(Guid product, DateOnly from, DateOnly to, string language, CancellationToken ct)
    {
        var moves = await stock.ItemTrackingAsync(product, from, to, language, ct).ConfigureAwait(false);
        var culture = Culture(language);
        var name = await db.Set<Product>().AsNoTracking().Where(p => p.Id == product).Select(p => new { p.Code, p.Name }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var (pdf, section) = Page(await RequestCompanyAsync(language, ct).ConfigureAwait(false),
            $"{Word(language, "itemTracking")} {name?.Code} {name?.Name.In(language)}".Trim(), Period(language, from, to, culture), language, landscape: true);
        var lines = moves.Select(m => ((IReadOnlyList<string>)[Day(m.PostingDate, culture), m.Document ?? "", m.DocumentType ?? "", m.From ?? "", m.To ?? "",
            Quantity(m.Quantity, culture), m.Unit ?? "", Amount(m.Value, culture)], false));
        Table(section, [(Word(language, "date"), 2.2, false), (Word(language, "document"), 2.6, false), (Word(language, "documentType"), 4.0, false),
            (Word(language, "from"), 4.4, false), (Word(language, "to"), 4.4, false), (Word(language, "quantity"), 2.4, true), (Word(language, "unit"), 2.0, false),
            (Word(language, "value"), 2.8, true)], lines);
        return new PdfFile(Render(pdf), $"item-tracking-{FileNamePart(name?.Code ?? "product")}-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
    }

    public async Task<PdfFile> LinesAsync(IReadOnlyList<Guid> categories, DateOnly from, DateOnly to, Guid? costCenter, Guid? partner, string language, CancellationToken ct)
    {
        var report = await stock.LinesAsync(categories, from, to, costCenter, partner, language, ct).ConfigureAwait(false);
        var culture = Culture(language);
        var (pdf, section) = Page(await RequestCompanyAsync(language, ct).ConfigureAwait(false), Word(language, "lines"), Period(language, from, to, culture), language,
            landscape: true);
        var lines = report.Lines.Select(l => ((IReadOnlyList<string>)[Day(l.PostingDate, culture), l.Document ?? "", l.Partner ?? "", l.CostCenter ?? "", l.Item ?? "",
            Quantity(l.Quantity, culture), Amount(l.Value, culture), Amount(l.Vat, culture), Amount(l.Total, culture)], false))
            .Append(([string.Empty, Word(language, "total"), "", "", "", "", Amount(report.Value, culture), Amount(report.Vat, culture), Amount(report.Total, culture)], true));
        Table(section, [(Word(language, "date"), 2.2, false), (Word(language, "document"), 2.4, false), (Word(language, "partner"), 4.2, false),
            (Word(language, "costCenter"), 3.4, false), (Word(language, "item"), 4.4, false), (Word(language, "quantity"), 2.0, true), (Word(language, "value"), 2.4, true),
            (Word(language, "vat"), 2.0, true), (Word(language, "total"), 2.4, true)], lines);
        return new PdfFile(Render(pdf), $"lines-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
    }

    public async Task<PdfFile?> PartnerLedgerAsync(Guid partner, DateOnly from, DateOnly to, string language, CancellationToken ct)
    {
        var name = await db.Set<Partner>().AsNoTracking().Where(p => p.Id == partner).Select(p => new { p.Code, p.Name }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (name is null)
        {
            return null;
        }

        var rows = await partners.LedgerAsync(partner, from, to, ct).ConfigureAwait(false);
        var culture = Culture(language);
        var (pdf, section) = Page(await RequestCompanyAsync(language, ct).ConfigureAwait(false),
            $"{Word(language, "partnerLedger")} {name.Code} {name.Name.In(language)}".Trim(), Period(language, from, to, culture), language);
        var lines = rows.Select(r => ((IReadOnlyList<string>)[Day(r.PostingDate, culture), r.Code ?? "", r.Category ?? "", Amount(r.Debit, culture), Amount(r.Credit, culture),
            Amount(r.Balance, culture)], false))
            .Append(([string.Empty, Word(language, "total"), "", Amount(rows.Sum(r => r.Debit), culture), Amount(rows.Sum(r => r.Credit), culture),
                Amount(rows.Count > 0 ? rows[^1].Balance : 0, culture)], true));
        Table(section, [(Word(language, "date"), 2.4, false), (Word(language, "document"), 3.0, false), (Word(language, "category"), 4.0, false),
            (Word(language, "debit"), 2.6, true), (Word(language, "credit"), 2.6, true), (Word(language, "balance"), 2.8, true)], lines);
        return new PdfFile(Render(pdf), $"partner-ledger-{FileNamePart(name.Code ?? "partner")}-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
    }

    public async Task<PdfFile?> CostCenterLedgerAsync(Guid costCenter, DateOnly from, DateOnly to, string language, CancellationToken ct)
    {
        if (await reports.CostCenterLedgerAsync(costCenter, from, to, ct).ConfigureAwait(false) is not { } rows)
        {
            return null;
        }

        var culture = Culture(language);
        var name = await db.Set<CostCenter>().AsNoTracking().Where(c => c.Id == costCenter).Select(c => new { c.Code, c.Name }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var (pdf, section) = Page(await RequestCompanyAsync(language, ct).ConfigureAwait(false),
            $"{Word(language, "costCenterLedger")} {name?.Code} {name?.Name.In(language)}".Trim(), Period(language, from, to, culture), language);
        var lines = rows.Select(r => ((IReadOnlyList<string>)[Day(r.PostingDate, culture), r.Document ?? "", Amount(r.Debit, culture), Amount(r.Credit, culture),
            Amount(r.Balance, culture)], false))
            .Append(([string.Empty, Word(language, "total"), Amount(rows.Sum(r => r.Debit), culture), Amount(rows.Sum(r => r.Credit), culture),
                Amount(rows.Count > 0 ? rows[^1].Balance : 0, culture)], true));
        Table(section, [(Word(language, "date"), 2.4, false), (Word(language, "document"), 4.0, false), (Word(language, "debit"), 3.0, true),
            (Word(language, "credit"), 3.0, true), (Word(language, "balance"), 3.2, true)], lines);
        return new PdfFile(Render(pdf), $"cost-centre-ledger-{FileNamePart(name?.Code ?? "cost-centre")}-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
    }

    public async Task<PdfFile> PartnerRankingAsync(Guid category, DateOnly? from, DateOnly? to, string language, CancellationToken ct)
    {
        var ranking = await partners.RankingAsync(category, from, to, 1, 500, ct).ConfigureAwait(false);
        var culture = Culture(language);
        // The ranking is of the partners' documents of one document category (e.g. sales), as PartnerStatisticsService.RankingAsync reads it.
        var categoryName = await db.Set<DocumentCategory>().AsNoTracking().Where(c => c.Id == category).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var (pdf, section) = Page(await RequestCompanyAsync(language, ct).ConfigureAwait(false),
            $"{Word(language, "partnerStatistics")} — {categoryName?.In(language)}", from is { } f && to is { } t ? Period(language, f, t, culture) : null, language);
        var lines = ranking.Items.Select((r, i) => ((IReadOnlyList<string>)[(i + 1).ToString(culture), r.Name ?? "", r.Documents.ToString(culture),
            Amount(r.Debit, culture), Amount(r.Credit, culture), Amount(r.Balance, culture)], false));
        Table(section, [("#", 1.0, true), (Word(language, "partner"), 6.6, false), (Word(language, "documents"), 2.4, true), (Word(language, "debit"), 2.6, true),
            (Word(language, "credit"), 2.6, true), (Word(language, "balance"), 2.8, true)], lines);
        return new PdfFile(Render(pdf), $"partners-{FileNamePart(categoryName?.In("en") ?? "category")}.pdf");
    }

    private static string Quantity(decimal value, System.Globalization.CultureInfo culture) => value.ToString("0.###", culture);
}

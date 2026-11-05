using System.Globalization;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Reports;

/// <summary>A row of a chart or a top list: a month, partner, product or cost centre, with the second category's amount on a comparison.</summary>
public sealed record DashboardRowDto(string? Name, decimal Value, decimal? Second, Guid? Id);

/// <summary>A card or chart as worked out for the period: a card has its value, a total also the previous period, a chart or list its rows.</summary>
public sealed record DashboardItemDto(Guid Id, string? Code, string? Name, string? Kind, decimal? Value, decimal? Previous, string? FirstName, string? SecondName, Guid? DocumentCategoryId,
    Guid? SecondCategoryId, bool Negate, IReadOnlyList<DashboardRowDto> Rows);

public sealed record DashboardDto(DateOnly From, DateOnly To, DateOnly PreviousFrom, DateOnly PreviousTo, string? Currency, IReadOnlyList<DashboardItemDto> Items);

/// <summary>
/// The dashboard: the items the company has configured,
/// each worked out from the same lines as its report, so the two never differ. Posted documents, net of VAT, in the company's currency, reversals
/// subtracted. Whoever has the screen in their menu may read it (Menu access).
/// </summary>
public sealed class DashboardService(ErpDbContext db, Visibility visibility, StockReportService lines)
{
    /// <summary>The menu screen that opens this: Menu access on its row decides who reads it.</summary>
    public const string Screen = "dashboard";

    public const string Total = nameof(Total), Balance = nameof(Balance), Drafts = nameof(Drafts), Monthly = nameof(Monthly), MonthlyVat = nameof(MonthlyVat),
        TopPartners = nameof(TopPartners), TopProducts = nameof(TopProducts), ByCostCenter = nameof(ByCostCenter), Overdue = nameof(Overdue), GrossProfit = nameof(GrossProfit),
        GrossProfitByCostCenter = nameof(GrossProfitByCostCenter), TopCombined = nameof(TopCombined);

    /// <summary>The kinds the engine can work out.</summary>
    public static readonly IReadOnlyList<string> Kinds = [Total, Balance, Drafts, Monthly, MonthlyVat, TopPartners, TopProducts, ByCostCenter, Overdue, GrossProfit, GrossProfitByCostCenter, TopCombined];

    /// <summary>How many rows a top list shows.</summary>
    public const int TopRows = 10;

    private const int MonthsShown = 12;

    public async Task<DashboardDto> DashboardAsync(DateOnly from, DateOnly to, Guid? costCenterId, string language, CancellationToken ct)
    {
        if (!await visibility.OpensScreenAsync(Screen, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: the dashboard is not in your menu.");
        }

        if (to < from)
        {
            throw ValidationException.For("to", "The period ends before it starts.");
        }

        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var items = await db.Set<DashboardItem>().AsNoTracking().Where(i => i.OwnerId != null && nodes.Contains(i.OwnerId.Value)).OrderBy(i => i.SortOrder).ThenBy(i => i.Code)
            .ToListAsync(ct).ConfigureAwait(false);
        var categoryIds = items.SelectMany(i => new[] { i.DocumentCategoryId, i.SecondCategoryId }).OfType<Guid>().Distinct().ToList();
        var names = await db.Set<DocumentCategory>().AsNoTracking().Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name.In(language), ct).ConfigureAwait(false);

        var (previousFrom, previousTo) = PreviousPeriod(from, to);
        var read = new Dictionary<(Guid, DateOnly, DateOnly), DocumentLinesDto>();
        async Task<DocumentLinesDto> LinesOf(Guid category, DateOnly since, DateOnly until)
        {
            if (!read.TryGetValue((category, since, until), out var found))
            {
                found = read[(category, since, until)] = await lines.LinesAsync([category], since, until, costCenterId, null, language, ct).ConfigureAwait(false);
            }

            return found;
        }

        var shown = new List<DashboardItemDto>();
        foreach (var item in items)
        {
            decimal? value = null, previous = null;
            IReadOnlyList<DashboardRowDto> rows = [];
            switch (item.Kind)
            {
                case Total when item.DocumentCategoryId is { } category:
                    (value, previous) = ((await LinesOf(category, from, to).ConfigureAwait(false)).Value, (await LinesOf(category, previousFrom, previousTo).ConfigureAwait(false)).Value);
                    break;
                case Balance when item.PartnerCategoryId is { } partners:
                    value = await BalanceAsync(partners, to, ct).ConfigureAwait(false) * (item.Negate ? -1 : 1);
                    break;
                case Drafts:
                    value = await DraftsAsync(item, ct).ConfigureAwait(false);
                    break;
                case Overdue:
                    // What clients owe past its due date; on Negate, what is owed to suppliers.
                    value = (await new Documents.OpenItems(db, visibility).ListAsync(to, null, ct).ConfigureAwait(false)).Where(i => i.DaysOverdue > 0 && i.Receivable != item.Negate).Sum(i => i.Open);
                    break;
                case GrossProfit when item.DocumentCategoryId is { } category:
                    (value, previous) = (Profit((await LinesOf(category, from, to).ConfigureAwait(false)).Lines), Profit((await LinesOf(category, previousFrom, previousTo).ConfigureAwait(false)).Lines));
                    break;
                case GrossProfitByCostCenter when item.DocumentCategoryId is { } category:
                    rows = [.. (await LinesOf(category, from, to).ConfigureAwait(false)).Lines.Where(l => l.CostCenter is not null).GroupBy(l => (l.CostCenterId, l.CostCenter))
                        .Select(g => new DashboardRowDto(g.Key.CostCenter, Profit(g), null, g.Key.CostCenterId)).OrderByDescending(r => r.Value).ThenBy(r => r.Name, StringComparer.Ordinal).Take(TopRows)];
                    break;
                case TopCombined when item.DocumentCategoryId is { } category:
                    var sold = (await LinesOf(category, from, to).ConfigureAwait(false)).Lines.Where(l => l.ItemId is not null).ToList();
                    var soldIds = sold.Select(l => l.ItemId!.Value).Distinct().ToList();
                    var combined = (await db.Set<Models.Products.Product>().AsNoTracking().Where(p => p.IsComposite && soldIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
                    rows = [.. sold.Where(l => combined.Contains(l.ItemId!.Value)).GroupBy(l => l.Item).Select(g => new DashboardRowDto(g.Key, g.Sum(l => l.Value), null, null))
                        .OrderByDescending(r => r.Value).ThenBy(r => r.Name, StringComparer.Ordinal).Take(TopRows)];
                    break;
                case Monthly or MonthlyVat when item.DocumentCategoryId is { } category:
                    rows = await MonthsAsync(item, category, to, LinesOf).ConfigureAwait(false);
                    break;
                case TopPartners when item.DocumentCategoryId is { } category:
                    rows = Top((await LinesOf(category, from, to).ConfigureAwait(false)).ByPartner);
                    break;
                case ByCostCenter when item.DocumentCategoryId is { } category:
                    rows = Top((await LinesOf(category, from, to).ConfigureAwait(false)).ByCostCenter);
                    break;
                case TopProducts when item.DocumentCategoryId is { } category:
                    rows = [.. (await LinesOf(category, from, to).ConfigureAwait(false)).Lines.GroupBy(l => l.Item).Select(g => new DashboardRowDto(g.Key, g.Sum(l => l.Value), null, null))
                        .OrderByDescending(r => r.Value).ThenBy(r => r.Name, StringComparer.Ordinal).Take(TopRows)];
                    break;
            }

            shown.Add(new DashboardItemDto(item.Id, item.Code, item.Name?.In(language), item.Kind, value, previous, NameOf(item.DocumentCategoryId), NameOf(item.SecondCategoryId),
                item.DocumentCategoryId, item.SecondCategoryId, item.Negate, rows));
        }

        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var currency = await db.Set<Currency>().AsNoTracking().Where(c => c.Active && c.OwnerId == company && c.IsBase).Select(c => c.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new DashboardDto(from, to, previousFrom, previousTo, currency, shown);

        string? NameOf(Guid? category) => category is { } id ? names.GetValueOrDefault(id) : null;
    }

    /// <summary>
    /// The period a total is set against. From the first of a month it is the same stretch of months before, so this month so far meets last month up to
    /// the same day and a whole month the whole month before; any other period is the same number of days just before it.
    /// </summary>
    public static (DateOnly From, DateOnly To) PreviousPeriod(DateOnly from, DateOnly to)
    {
        if (from.Day != 1)
        {
            var days = to.DayNumber - from.DayNumber + 1;
            return (from.AddDays(-days), from.AddDays(-1));
        }

        var months = ((to.Year - from.Year) * 12) + to.Month - from.Month + 1;
        var end = to.AddMonths(-months);
        var wholeMonth = to.Day == DateTime.DaysInMonth(to.Year, to.Month);
        return (from.AddMonths(-months), wholeMonth ? new DateOnly(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month)) : end);
    }

    // What was earned over what the lines cost, net of VAT.
    private static decimal Profit(IEnumerable<DocumentLineRowDto> lines) => lines.Sum(l => l.Value - l.Cost);

    // Partners with the biggest amounts first; a row with no partner or cost centre is not one.
    private static List<DashboardRowDto> Top(IReadOnlyList<LineGroupDto> groups) =>
        [.. groups.Where(g => g.Name is not null).Select(g => new DashboardRowDto(g.Name, g.Value, null, g.Id)).OrderByDescending(r => r.Value).ThenBy(r => r.Name, StringComparer.Ordinal).Take(TopRows)];

    // What the partners of a category stood at on a day, debit less credit, from the balance rows of the posted documents the request sees.
    private async Task<decimal> BalanceAsync(Guid categoryId, DateOnly until, CancellationToken ct)
    {
        var nodes = (await visibility.NodesAsync(ct).ConfigureAwait(false)).ToList();
        var inCategory = db.Set<InterPartnerCategory>().Where(l => l.CategoryId == categoryId).Select(l => l.ElementId);
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var total = 0m;
        foreach (var period in await Gamma.Erp.Services.Currencies.CurrencyHistory.PeriodsAsync(db, companies, until, ct).ConfigureAwait(false))
        {
            var (since, before) = (period.From, period.Until);
            total += Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(await (from b in db.Set<PartnerBalance>().AsNoTracking()
                                     join d in db.Set<Document>().AsNoTracking() on b.DocumentId equals d.Id
                                     where d.Posted && d.PostingDate <= until && d.OwnerId != null && nodes.Contains(d.OwnerId.Value) && b.PartnerId != null
                                         && inCategory.Contains(b.PartnerId.Value) && (since == null || d.PostingDate >= since) && (before == null || d.PostingDate < before)
                                     select (b.DebitLcu ?? 0) - (b.CreditLcu ?? 0)).SumAsync(ct).ConfigureAwait(false), period.Factor);
        }

        return total;
    }

    // The documents not posted yet, of the item's categories, or of all with none named.
    private async Task<decimal> DraftsAsync(DashboardItem item, CancellationToken ct)
    {
        var nodes = (await visibility.NodesAsync(ct).ConfigureAwait(false)).ToList();
        var categories = new[] { item.DocumentCategoryId, item.SecondCategoryId }.OfType<Guid>().ToList();
        var documents = db.Set<Document>().AsNoTracking().Where(d => !d.Posted && d.OwnerId != null && nodes.Contains(d.OwnerId.Value));
        if (categories.Count > 0)
        {
            var inCategories = db.Set<InterDocumentCategory>().Where(l => categories.Contains(l.CategoryId)).Select(l => l.ElementId);
            documents = documents.Where(d => inCategories.Contains(d.Id));
        }

        return await documents.CountAsync(ct).ConfigureAwait(false);
    }

    // The twelve months up to the end of the period, an empty month as zero.
    private static async Task<IReadOnlyList<DashboardRowDto>> MonthsAsync(DashboardItem item, Guid category, DateOnly to, Func<Guid, DateOnly, DateOnly, Task<DocumentLinesDto>> linesOf)
    {
        var since = new DateOnly(to.Year, to.Month, 1).AddMonths(1 - MonthsShown);
        var first = await linesOf(category, since, to).ConfigureAwait(false);
        var second = item.SecondCategoryId is { } other ? await linesOf(other, since, to).ConfigureAwait(false) : null;
        Func<DocumentLineRowDto, decimal> amount = item.Kind == MonthlyVat ? l => l.Vat : l => l.Value;
        decimal In(DocumentLinesDto of, DateOnly month) => of.Lines.Where(l => l.PostingDate is { } d && d.Year == month.Year && d.Month == month.Month).Sum(amount);
        return [.. Enumerable.Range(0, MonthsShown).Select(i => since.AddMonths(i)).Select(m =>
            new DashboardRowDto(m.ToString("yyyy-MM", CultureInfo.InvariantCulture), In(first, m), second is null ? null : In(second, m), null))];
    }
}

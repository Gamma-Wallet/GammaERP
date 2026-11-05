using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.Posting;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Reports;

/// <summary>A line of a statement, this period and the same period a year before.</summary>
public sealed record StatementRowDto(Guid Id, Guid? ParentId, string? Code, string? Name, int Level, bool IsHeading, decimal Amount, decimal Previous);

/// <summary>An account with a balance that sits on no line: the statement does not show it.</summary>
public sealed record StatementAccountDto(string? Code, string? Name, decimal Balance);

/// <summary>A balance sheet at a day: Difference is the two sides' gap, nought when the lines hold every account.</summary>
public sealed record BalanceSheetDto(DateOnly To, IReadOnlyList<StatementRowDto> Rows, decimal Assets, decimal LiabilitiesAndEquity, decimal Difference, IReadOnlyList<StatementAccountDto> Unplaced);

/// <summary>Profit and loss of a period, with the same period a year before.</summary>
public sealed record ProfitLossDto(DateOnly From, DateOnly To, IReadOnlyList<StatementRowDto> Rows, decimal Result, decimal PreviousResult, IReadOnlyList<StatementAccountDto> Unplaced);

/// <summary>
/// The financial statements.
/// The lines and which accounts they hold are configuration (<see cref="StatementLine"/>); an account on no line is listed, never dropped.
/// </summary>
public sealed partial class ReportService
{
    /// <summary>The balance sheet at a day, beside the same day a year before.</summary>
    public async Task<BalanceSheetDto> BalanceSheetAsync(DateOnly to, Guid? chart, string language, CancellationToken ct)
    {
        var lines = await LinesAsync<ChartAssetsLiabilities>(ct).ConfigureAwait(false);
        var profitLoss = await LinesAsync<ChartProfitLoss>(ct).ConfigureAwait(false);
        var (now, unplaced) = await SheetAmountsAsync(lines, profitLoss, to, chart, ct).ConfigureAwait(false);
        var (before, _) = await SheetAmountsAsync(lines, profitLoss, to.AddYears(-1), chart, ct).ConfigureAwait(false);
        var rows = Rows(lines, now, before, language);
        var (assets, other) = (Roots(lines, now, credit: false), Roots(lines, now, credit: true));
        return new BalanceSheetDto(to, rows, assets, other, assets - other, unplaced);
    }

    /// <summary>Profit and loss of a period, beside the same period a year before.</summary>
    public async Task<ProfitLossDto> ProfitLossAsync(DateOnly from, DateOnly to, Guid? chart, string language, CancellationToken ct)
    {
        Period(from, to);
        var lines = await LinesAsync<ChartProfitLoss>(ct).ConfigureAwait(false);
        var sheet = await LinesAsync<ChartAssetsLiabilities>(ct).ConfigureAwait(false);
        async Task<(Dictionary<Guid, decimal> Amounts, List<StatementAccountDto> Unplaced)> In(DateOnly start, DateOnly end)
        {
            var amounts = lines.ToDictionary(l => l.Id, _ => 0m);
            var unplaced = new List<StatementAccountDto>();
            foreach (var account in await BalancesAsync(start, end, chart, ct).ConfigureAwait(false))
            {
                if (Place(lines, account) is { } line)
                {
                    amounts[line.Id] += Shown(line, account.Balance);
                }
                else if (Place(sheet, account) is null)
                {
                    unplaced.Add(new StatementAccountDto(account.Code, account.Name, account.Balance));
                }
            }

            return (RolledUp(lines, amounts), unplaced);
        }

        var (now, unplaced) = await In(from, to).ConfigureAwait(false);
        var (before, _) = await In(from.AddYears(-1), to.AddYears(-1)).ConfigureAwait(false);
        decimal Result(Dictionary<Guid, decimal> amounts) => Roots(lines, amounts, credit: true) - Roots(lines, amounts, credit: false);
        return new ProfitLossDto(from, to, Rows(lines, now, before, language), Result(now), Result(before), unplaced);
    }

    private sealed record AccountBalance(string? Code, string? Name, decimal Balance);

    private async Task<(Dictionary<Guid, decimal> Amounts, List<StatementAccountDto> Unplaced)> SheetAmountsAsync(IReadOnlyList<StatementLine> lines, IReadOnlyList<StatementLine> profitLoss,
        DateOnly to, Guid? chart, CancellationToken ct)
    {
        var yearStart = await FiscalYearStartAsync(to, ct).ConfigureAwait(false);
        var amounts = lines.ToDictionary(l => l.Id, _ => 0m);
        var unplaced = new List<StatementAccountDto>();
        foreach (var account in await BalancesAsync(null, to, chart, ct).ConfigureAwait(false))
        {
            if (Place(lines, account) is { } line)
            {
                amounts[line.Id] += Shown(line, account.Balance);
            }
            else if (Place(profitLoss, account) is null)
            {
                unplaced.Add(new StatementAccountDto(account.Code, account.Name, account.Balance));
            }
        }

        // The results are what the profit and loss accounts hold: no year-end closing moves them yet.
        decimal Result(IReadOnlyList<AccountBalance> balances) => balances.Where(a => Place(lines, a) is null && Place(profitLoss, a) is not null).Sum(a => -a.Balance);
        var earlier = Result(await BalancesAsync(null, yearStart.AddDays(-1), chart, ct).ConfigureAwait(false));
        var period = Result(await BalancesAsync(yearStart, to, chart, ct).ConfigureAwait(false));
        foreach (var line in lines.Where(l => l.ResultKind != 0))
        {
            amounts[line.Id] = line.ResultKind == 1 ? period : earlier;
        }

        return (RolledUp(lines, amounts), unplaced);
    }

    /// <summary>The company's lines of a statement, in order.</summary>
    private async Task<List<StatementLine>> LinesAsync<T>(CancellationToken ct)
        where T : StatementLine
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        return [.. (await db.Set<T>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).Where(l => l.OwnerId is null || nodes.Contains(l.OwnerId.Value))
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Code, StringComparer.Ordinal).Cast<StatementLine>()];
    }

    /// <summary>Each account's debit less credit, posted between two days (from the start when no first day is given).</summary>
    /// <summary>The accounts' balances in one chart category (tree): the one chosen, else the statutory one, else every chart.</summary>
    private async Task<IReadOnlyList<AccountBalance>> BalancesAsync(DateOnly? from, DateOnly to, Guid? chart, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        // One tree at a time (posting groups may post to several): the chosen one, else the statutory one as the trial balance; with none marked, every chart.
        var statutory = chart is { } chosen ? [chosen] : await db.Set<ChartCategory>().AsNoTracking()
            .Where(c => c.Active && c.IsStatutory && c.OwnerId != null && companies.Contains(c.OwnerId.Value)).Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        var inChart = db.Set<InterChartCategory>().Where(l => l.Active && statutory.Contains(l.CategoryId)).Select(l => (Guid?)l.ElementId);
        // Each currency period at its factor: what was posted before a change of the company's currency, at its rate.
        var all = new List<(Guid ChartId, decimal Balance)>();
        foreach (var period in await Gamma.Erp.Services.Currencies.CurrencyHistory.PeriodsAsync(db, companies, to, ct).ConfigureAwait(false))
        {
            var (since, until, factor) = (period.From, period.Until, period.Factor);
            all.AddRange((await db.Set<ChartValue>().AsNoTracking().Where(v => v.Active && v.ChartId != null && companies.Contains(v.CompanyId) && v.PostingDate <= to
                    && (from == null || v.PostingDate >= from) && (statutory.Count == 0 || inChart.Contains(v.ChartId)) && (since == null || v.PostingDate >= since)
                    && (until == null || v.PostingDate < until))
                .GroupBy(v => v.ChartId).Select(g => new { ChartId = g.Key!.Value, Balance = g.Sum(v => (v.Debit ?? 0) - (v.Credit ?? 0)) }).ToListAsync(ct).ConfigureAwait(false))
                .Select(x => (x.ChartId, Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(x.Balance, factor))));
        }

        var sums = all.GroupBy(x => x.ChartId).Select(g => new { ChartId = g.Key, Balance = g.Sum(x => x.Balance) }).ToList();
        var ids = sums.Select(s => s.ChartId).ToList();
        var accounts = await db.Set<Chart>().AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        return [.. sums.Where(s => s.Balance != 0).Select(s => new AccountBalance(accounts[s.ChartId].Code, accounts[s.ChartId].Name.In(Models.Base.TranslatedText.DefaultLanguage), s.Balance))];
    }

    /// <summary>The line that holds an account: the longest start of code that fits, among lines whose balance rule the account meets.</summary>
    private static StatementLine? Place(IReadOnlyList<StatementLine> lines, AccountBalance account)
    {
        var (best, length) = ((StatementLine?)null, 0);
        foreach (var line in lines.Where(l => l.ResultKind == 0 && !string.IsNullOrWhiteSpace(l.Accounts)
            && (l.BalanceSide == 0 || (l.BalanceSide == 1 ? account.Balance > 0 : account.Balance < 0))))
        {
            foreach (var start in line.Accounts!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (start.Length > length && account.Code?.StartsWith(start, StringComparison.Ordinal) == true)
                {
                    (best, length) = (line, start.Length);
                }
            }
        }

        return best;
    }

    private static decimal Shown(StatementLine line, decimal balance) => line.CreditSide ? -balance : balance;

    /// <summary>Each heading totals everything below it, at every level, its sub-lines on its own side.</summary>
    private static Dictionary<Guid, decimal> RolledUp(IReadOnlyList<StatementLine> lines, Dictionary<Guid, decimal> own)
    {
        var byId = lines.ToDictionary(l => l.Id);
        var total = lines.ToDictionary(l => l.Id, _ => 0m);
        foreach (var line in lines)
        {
            for (Guid? id = line.Id; id is { } at && byId.TryGetValue(at, out var row); id = row.IdSup)
            {
                total[at] += own[line.Id] * (row.CreditSide == line.CreditSide ? 1 : -1);
            }
        }

        return total;
    }

    private static decimal Roots(IReadOnlyList<StatementLine> lines, Dictionary<Guid, decimal> amounts, bool credit) =>
        lines.Where(l => l.IdSup is null && l.CreditSide == credit).Sum(l => amounts[l.Id]);

    private static List<StatementRowDto> Rows(IReadOnlyList<StatementLine> lines, Dictionary<Guid, decimal> now, Dictionary<Guid, decimal> before, string language)
    {
        var rows = new List<StatementRowDto>();
        void Add(Guid? parent, int level)
        {
            foreach (var line in lines.Where(l => l.IdSup == parent))
            {
                rows.Add(new StatementRowDto(line.Id, parent, line.Code, line.Name?.In(language), level, lines.Any(l => l.IdSup == line.Id), now[line.Id], before[line.Id]));
                Add(line.Id, level + 1);
            }
        }

        Add(null, 0);
        return rows;
    }
}

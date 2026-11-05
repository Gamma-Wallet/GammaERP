using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Currencies;

/// <summary>Amounts posted from <see cref="From"/> (empty: from the start) up to before <see cref="Until"/> (empty: on), read times <see cref="Factor"/>.</summary>
public sealed record CurrencyPeriod(DateOnly? From, DateOnly? Until, decimal Factor)
{
    /// <summary>Whether a day falls in the period.</summary>
    public bool Holds(DateOnly? day) => day is not { } d || ((From is null || d >= From) && (Until is null || d < Until));
}

/// <summary>
/// The company's currency over time (tbl_company_currency, the founder, 2026-11-04): what was posted before a change stays as written, and a
/// report read on a day after it converts it at the change's rate. As at a day before any change, one period with factor 1: nothing changes.
/// </summary>
public static class CurrencyHistory
{
    /// <summary>The periods as at a day, oldest first; the last has factor 1 (the currency of that day).</summary>
    public static async Task<IReadOnlyList<CurrencyPeriod>> PeriodsAsync(ErpDbContext db, IReadOnlyCollection<Guid> companies, DateOnly asOf, CancellationToken ct)
    {
        var changes = await db.Set<CompanyCurrency>().AsNoTracking().Where(c => c.Active && companies.Contains(c.CompanyId) && c.FromDate <= asOf)
            .OrderBy(c => c.FromDate).ToListAsync(ct).ConfigureAwait(false);
        if (changes.Count == 0)
        {
            return [new CurrencyPeriod(null, null, 1)];
        }

        var periods = new List<CurrencyPeriod>();
        DateOnly? from = null;
        foreach (var (change, i) in changes.Select((c, i) => (c, i)))
        {
            // Divided by each later change's rate: one new unit is worth Rate previous units.
            periods.Add(new CurrencyPeriod(from, change.FromDate, changes.Skip(i).Aggregate(1m, (factor, c) => factor / c.Rate)));
            from = change.FromDate;
        }

        periods.Add(new CurrencyPeriod(from, null, 1));
        return periods;
    }

    /// <summary>An amount read at a factor: itself at 1, else to the cent.</summary>
    public static decimal Scale(decimal amount, decimal factor) => factor == 1 ? amount : Math.Round(amount * factor, 2, MidpointRounding.AwayFromZero);

    /// <summary>The factor of an amount posted on a day, among the periods.</summary>
    public static decimal FactorOn(IReadOnlyList<CurrencyPeriod> periods, DateOnly? day) => periods.FirstOrDefault(p => p.Holds(day))?.Factor ?? 1;
}

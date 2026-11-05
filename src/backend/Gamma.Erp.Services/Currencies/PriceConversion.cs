using Gamma.Erp.Persistence;

namespace Gamma.Erp.Services.Currencies;

/// <summary>
/// A price in one currency as another on a day (the founder, 2026-11-04): a stored value in the company's currency (Value LCU) is right only on the day it
/// was set, so a price is always worked out again from its value at the rates of the day: value × its currency's rate ÷ the other currency's rate.
/// </summary>
public static class PriceConversion
{
    /// <summary>The value in <paramref name="to"/> (none: the company's currency); null when a currency has no rate on or before the day.</summary>
    public static async Task<decimal?> ConvertAsync(ErpDbContext db, decimal value, Guid? from, Guid? to, Guid baseCurrency, DateOnly day, CancellationToken ct)
    {
        var (source, target) = (from ?? baseCurrency, to ?? baseCurrency);
        if (source == target)
        {
            return value;
        }

        var sourceRate = source == baseCurrency ? 1 : await CurrencyService.RateOnAsync(db, source, baseCurrency, day, ct).ConfigureAwait(false);
        var targetRate = target == baseCurrency ? 1 : await CurrencyService.RateOnAsync(db, target, baseCurrency, day, ct).ConfigureAwait(false);
        return sourceRate is { } s && targetRate is { } t && t != 0 ? Math.Round(value * s / t, 4) : null;
    }
}

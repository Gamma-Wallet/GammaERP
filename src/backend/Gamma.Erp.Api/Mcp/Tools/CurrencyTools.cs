using System.ComponentModel;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Currencies;
using Gamma.Erp.Services.Entities;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>An exchange rate as Claude gives it.</summary>
public sealed record RateInput(
    [property: Description("The currency: code (e.g. EUR) or id.")] string Currency,
    [property: Description("The day (yyyy-mm-dd).")] DateOnly Date,
    [property: Description("One unit of the currency in the company's own currency, above 0.")] decimal Rate);

/// <summary>Currencies and their daily rates, behind the Currencies screen as their routes are; setting rates is for administrators, as there.</summary>
[McpServerToolType]
public sealed class CurrencyTools(CurrencyService currencies, ErpDbContext db, RequestContext request, Visibility visibility)
{
    private const string Screen = "currencies";

    [McpServerTool(Name = "list_currencies", Title = "Currencies", ReadOnly = true)]
    [Description("The company's currencies: id, code, name, and which is its own (base) currency, the LCU.")]
    public Task<object> ListCurrenciesAsync(CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, Screen, ct).ConfigureAwait(false);
            return await currencies.CurrenciesAsync(Registers.English, ct).ConfigureAwait(false);
        });

    [McpServerTool(Name = "exchange_rates", Title = "Exchange rates of a currency", ReadOnly = true)]
    [Description("A currency's saved daily rates in a period (one unit in the company's own currency), oldest first. A document of a day takes the latest rate on or before that day.")]
    public Task<object> ExchangeRatesAsync([Description("The currency: code or id.")] string currency,
        [Description("Optional: from (yyyy-mm-dd).")] DateOnly? from = null, [Description("Optional: to (yyyy-mm-dd).")] DateOnly? to = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, Screen, ct).ConfigureAwait(false);
            var id = await CurrencyAsync(currency, ct).ConfigureAwait(false);
            var rates = await currencies.RatesAsync(id, from, to, ct).ConfigureAwait(false);
            return rates.Select(r => new { date = r.RateDate, r.Rate });
        });

    [McpServerTool(Name = "set_exchange_rates", Title = "Save exchange rates", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Saves one or more daily exchange rates (a rate already saved for that day is changed, the old one kept as history). All are saved or none. Administrators only. First show the user the rates and where they come from and get an explicit yes; then confirm=true. Returns what was saved.")]
    public Task<object> SetExchangeRatesAsync(
        [Description("True only after the user said yes to these rates.")] bool confirm,
        [Description("The rates: currency, day, rate (one unit in the company's own currency).")] IReadOnlyList<RateInput> rates, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the rates to save and their source");
            Erp.NeedsPlace(request);
            await Registers.Screen(visibility, Screen, ct).ConfigureAwait(false);
            Registers.Administrator(request);
            if (rates is not { Count: > 0 })
            {
                throw new McpException("Give at least one rate.");
            }

            var ids = new List<Guid>();
            foreach (var rate in rates)
            {
                ids.Add(await CurrencyAsync(rate.Currency, ct).ConfigureAwait(false));
            }

            var saved = new List<object>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            foreach (var (rate, i) in rates.Select((r, i) => (r, i)))
            {
                var row = await currencies.SetRateAsync(new SetRateRequest(ids[i], rate.Date, rate.Rate), ct).ConfigureAwait(false);
                saved.Add(new { currency = rate.Currency.Trim().ToUpperInvariant(), date = row.RateDate, row.Rate });
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new { saved };
        });

    private async Task<Guid> CurrencyAsync(string given, CancellationToken ct)
    {
        var key = given.Trim();
        var all = await currencies.CurrenciesAsync(Registers.English, ct).ConfigureAwait(false);
        return all.FirstOrDefault(c => Registers.Is(c.Id, key) || string.Equals(c.Code, key, StringComparison.OrdinalIgnoreCase))?.Id
            ?? throw new McpException($"The company has no currency \"{key}\". It has: {string.Join(", ", all.Select(c => c.Code))}.");
    }
}

using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Currencies;

public sealed record CurrencyDto(Guid Id, string? Code, string? Name, bool IsBase);

/// <summary>A rate on a day: one unit of the currency in the company's base currency.</summary>
public sealed record ExchangeRateDto(Guid Id, Guid CurrencyId, DateOnly RateDate, decimal Rate);

public sealed record SetRateRequest(Guid CurrencyId, DateOnly RateDate, decimal Rate);

/// <summary>A new currency (no id: its code and name) or a currency renamed (its id: the name; a code stays what it is).</summary>
public sealed record SaveCurrencyRequest(Guid? Id, string? Code, string? Name);

/// <summary>
/// The company's currencies and their rates by day: a rate is one unit of a currency in the company's base currency;
/// a document in that currency takes the rate of its day (the latest on or before it) when none is typed.
/// </summary>
public sealed class CurrencyService(ErpDbContext db, RequestContext request, Visibility visibility)
{
    public async Task<IReadOnlyList<CurrencyDto>> CurrenciesAsync(string language, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        return [.. (await db.Set<Currency>().AsNoTracking().Where(c => c.Active && c.OwnerId == company).OrderByDescending(c => c.IsBase).ThenBy(c => c.Code)
            .ToListAsync(ct).ConfigureAwait(false)).Select(c => new CurrencyDto(c.Id, c.Code, c.Name?.In(language), c.IsBase))];
    }

    /// <summary>
    /// Adds a currency to the company, or renames one (the founder, 2026-11-01). A code is three capital
    /// letters, once in a company. Which one is the company's own was settled at sign-up and does not change here.
    /// </summary>
    public async Task<CurrencyDto> SaveCurrencyAsync(SaveCurrencyRequest currency, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(currency);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var name = currency.Name?.Trim();
        Currency row;
        if (currency.Id is { } id)
        {
            row = await db.Set<Currency>().FirstOrDefaultAsync(c => c.Id == id && c.Active && c.OwnerId == company, ct).ConfigureAwait(false)
                ?? throw ValidationException.For("id", "The company has no such currency.");
            row.Name = TranslatedText.English(string.IsNullOrEmpty(name) ? row.Code ?? string.Empty : name);
        }
        else
        {
            var code = currency.Code?.Trim().ToUpperInvariant() ?? string.Empty;
            if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
            {
                throw ValidationException.For("code", "A currency code is three letters, such as EUR.");
            }

            if (await db.Set<Currency>().AsNoTracking().AnyAsync(c => c.Active && c.OwnerId == company && c.Code == code, ct).ConfigureAwait(false))
            {
                throw ValidationException.For("code", $"The company already has {code}.");
            }

            row = new Currency { Code = code, Name = TranslatedText.English(string.IsNullOrEmpty(name) ? code : name), TenantId = request.TenantId, OwnerId = company };
            db.Add(row);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new CurrencyDto(row.Id, row.Code, row.Name?.In(TranslatedText.DefaultLanguage), row.IsBase);
    }

    /// <summary>Switches a currency off, when nothing uses it: the company's own never, one on a document, a line or a price neither.</summary>
    public async Task SwitchOffCurrencyAsync(Guid id, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<Currency>().FirstOrDefaultAsync(c => c.Id == id && c.Active && c.OwnerId == company, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The company has no such currency.");
        if (row.IsBase)
        {
            throw ValidationException.For("id", "The company's own currency can't be switched off.");
        }

        if (await db.Set<Transaction>().AsNoTracking().AnyAsync(t => t.CurrencyId == id, ct).ConfigureAwait(false)
            || await db.Set<DocumentTotal>().AsNoTracking().AnyAsync(t => t.CurrencyId == id, ct).ConfigureAwait(false)
            || await db.Set<ProductSalePrice>().AsNoTracking().AnyAsync(p => p.CurrencyId == id, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("id", $"{row.Code} is used on a document or a price, so it stays.");
        }

        row.Active = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExchangeRateDto>> RatesAsync(Guid currencyId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var (_, baseCurrency) = await PairAsync(currencyId, ct).ConfigureAwait(false);
        return [.. await db.Set<ExchangeRate>().AsNoTracking()
            .Where(r => r.Active && r.CurrencyId == currencyId && r.Currency2Id == baseCurrency && r.RateDate != null && (from == null || r.RateDate >= from) && (to == null || r.RateDate <= to))
            .OrderBy(r => r.RateDate).Select(r => new ExchangeRateDto(r.Id, currencyId, r.RateDate!.Value, r.Rate ?? 0)).ToListAsync(ct).ConfigureAwait(false)];
    }

    /// <summary>The day's rate: set on that day, or changed (a change keeps the old rate as history, as every row).</summary>
    public async Task<ExchangeRateDto> SetRateAsync(SetRateRequest rate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rate);
        if (rate.Rate <= 0)
        {
            throw ValidationException.For("rate", "A rate is above 0.");
        }

        var (currency, baseCurrency) = await PairAsync(rate.CurrencyId, ct).ConfigureAwait(false);
        if (currency.IsBase)
        {
            throw ValidationException.For("currencyId", "The base currency's rate is always 1.");
        }

        var row = await db.Set<ExchangeRate>().FirstOrDefaultAsync(r => r.Active && r.CurrencyId == currency.Id && r.Currency2Id == baseCurrency && r.RateDate == rate.RateDate, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            row = new ExchangeRate { CurrencyId = currency.Id, Currency2Id = baseCurrency, RateDate = rate.RateDate, TenantId = request.TenantId, OwnerId = currency.OwnerId };
            db.Add(row);
        }

        row.Rate = rate.Rate;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ExchangeRateDto(row.Id, currency.Id, rate.RateDate, rate.Rate);
    }

    /// <summary>The rate a document of that day takes: 1 for the base currency, else the latest on or before the day; null when there is none.</summary>
    public async Task<decimal?> RateAsync(Guid currencyId, DateOnly day, CancellationToken ct)
    {
        var (currency, baseCurrency) = await PairAsync(currencyId, ct).ConfigureAwait(false);
        return currency.IsBase ? 1 : await RateOnAsync(db, currency.Id, baseCurrency, day, ct).ConfigureAwait(false);
    }

    /// <summary>The latest rate of a currency in a base currency on or before a day (shared with the documents).</summary>
    internal static async Task<decimal?> RateOnAsync(ErpDbContext db, Guid currencyId, Guid baseCurrencyId, DateOnly day, CancellationToken ct) =>
        await db.Set<ExchangeRate>().AsNoTracking()
            .Where(r => r.Active && r.CurrencyId == currencyId && r.Currency2Id == baseCurrencyId && r.RateDate != null && r.RateDate <= day)
            .OrderByDescending(r => r.RateDate).Select(r => r.Rate).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    private async Task<(Currency Currency, Guid BaseCurrency)> PairAsync(Guid currencyId, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var currencies = await db.Set<Currency>().AsNoTracking().Where(c => c.Active && c.OwnerId == company).ToListAsync(ct).ConfigureAwait(false);
        var currency = currencies.FirstOrDefault(c => c.Id == currencyId) ?? throw ValidationException.For("currencyId", "The company has no such currency.");
        var baseCurrency = currencies.FirstOrDefault(c => c.IsBase) ?? throw ValidationException.For("currencyId", "The company has no base currency.");
        return (currency, baseCurrency.Id);
    }

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "Currencies belong to a company: choose one of its nodes.");
}

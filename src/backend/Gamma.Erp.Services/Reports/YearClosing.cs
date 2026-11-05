using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Reports;

/// <summary>Where a fiscal year stands: its days, what is locked already, the drafts left in it and whether its books agree.</summary>
public sealed record YearClosingDto(DateOnly From, DateOnly To, DateOnly? ClosedUntil, int Drafts, decimal Debit, decimal Credit, bool Closed, bool CanClose,
    Guid? CurrencyId = null, string? Currency = null);

/// <summary>
/// Closing a year (the founder, 2026-11-02): nothing is written to the books. Balances run on across years and the statements work out each period, so closing only checks the
/// year is finished and locks it. The lock is the row under Closed periods, where an administrator can move it back.
/// </summary>
public sealed class YearClosing(ErpDbContext db, RequestContext request, Visibility visibility, ReportService reports)
{
    /// <summary>The fiscal year a day falls in, as it stands.</summary>
    public async Task<YearClosingDto> CheckAsync(DateOnly day, CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("company", "Configuration belongs to a company: choose one of its nodes.");
        var from = await reports.FiscalYearStartAsync(day, ct).ConfigureAwait(false);
        var to = from.AddYears(1).AddDays(-1);
        var closed = await db.Set<PostingLock>().AsNoTracking().Where(l => l.OwnerId == company && l.Active).Select(l => (DateOnly?)l.LockedUntil).MaxAsync(ct).ConfigureAwait(false);
        var neverPosted = db.Set<DocumentType>().Where(t => t.NeverPosted).Select(t => t.Id);
        var drafts = await db.Set<Document>().AsNoTracking().CountAsync(d => d.Active && !d.Posted && d.CompanyId == company && !neverPosted.Contains(d.TypeId)
            && (d.PostingDate ?? d.DocumentDate) <= to, ct).ConfigureAwait(false);
        var books = await reports.TrialBalanceAsync(from, to, null, Models.Base.TranslatedText.DefaultLanguage, null, ct).ConfigureAwait(false);
        var locked = closed is { } until && until >= to;
        var currency = await db.Set<Currency>().AsNoTracking().FirstOrDefaultAsync(c => c.Active && c.IsBase && c.OwnerId == company, ct).ConfigureAwait(false);
        return new YearClosingDto(from, to, closed, drafts, books.TotalDebit, books.TotalCredit, locked, !locked && drafts == 0 && books.TotalDebit == books.TotalCredit,
            currency?.Id, currency?.Code);
    }

    /// <summary>
    /// Locks the year; refused while a draft is left in it or its books do not agree. With <paramref name="nextCurrency"/> (the founder,
    /// 2026-11-04) the company keeps its books in that currency from the next year's first day, at the exchange rate of that day: the old year
    /// stays as written, reports read after the change convert what was posted before (<see cref="Currencies.CurrencyHistory"/>), and the stock's
    /// open cost layers and the price lists are converted once, here.
    /// </summary>
    public async Task<YearClosingDto> CloseAsync(DateOnly day, CancellationToken ct, Guid? nextCurrency = null)
    {
        var year = await CheckAsync(day, ct).ConfigureAwait(false);
        if (year.Closed)
        {
            return year;
        }

        (Guid Old, Guid New, decimal Rate)? change = nextCurrency is { } next && next != year.CurrencyId ? await NewCurrencyAsync(year, next, ct).ConfigureAwait(false) : null;

        if (year.Drafts > 0)
        {
            throw ValidationException.For("drafts", $"{ErrorCodes.PeriodClosedError}: {year.Drafts} documents are not posted yet in this year: post them or switch them off first.");
        }

        if (year.Debit != year.Credit)
        {
            throw ValidationException.For("books", $"{ErrorCodes.PeriodClosedError}: the books of this year do not agree: debit {year.Debit} ≠ credit {year.Credit}.");
        }

        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var locks = await db.Set<PostingLock>().Where(l => l.OwnerId == company && l.Active).ToListAsync(ct).ConfigureAwait(false);
        if (locks.FirstOrDefault() is { } row)
        {
            row.LockedUntil = year.To;
        }
        else
        {
            db.Add(new PostingLock { OwnerId = company, TenantId = request.TenantId, LockedUntil = year.To });
        }

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        if (change is { } c)
        {
            await ChangeCurrencyAsync(company!.Value, c.Old, c.New, year.To.AddDays(1), c.Rate, ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return await CheckAsync(day, ct).ConfigureAwait(false);
    }

    /// <summary>The change asked for, checked: a currency of the company, and the rate old to new of the next year's first day.</summary>
    private async Task<(Guid Old, Guid New, decimal Rate)> NewCurrencyAsync(YearClosingDto year, Guid next, CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var currencies = await db.Set<Currency>().AsNoTracking().Where(c => c.Active && c.OwnerId == company).ToListAsync(ct).ConfigureAwait(false);
        var to = currencies.FirstOrDefault(c => c.Id == next) ?? throw ValidationException.For("currency", "The next year's currency is not one of the company's.");
        var from = currencies.FirstOrDefault(c => c.IsBase) ?? throw ValidationException.For("currency", "The company has no currency of its own.");
        var first = year.To.AddDays(1);
        // As rates are entered: the new currency in the company's (1 EUR = 97.50 ALL) of exactly that day.
        var rate = await db.Set<ExchangeRate>().AsNoTracking().Where(r => r.Active && r.CurrencyId == to.Id && r.Currency2Id == from.Id && r.RateDate == first && r.Rate > 0)
            .Select(r => r.Rate).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("currency", $"Enter the exchange rate of {to.Code} for {first:yyyy-MM-dd} first: the books change currency at that day's rate.");
        return (from.Id, to.Id, rate);
    }

    /// <summary>The company's currency changes from a day: the change kept, the company's currency moved, open stock and price lists converted.</summary>
    private async Task ChangeCurrencyAsync(Guid company, Guid old, Guid next, DateOnly first, decimal rate, CancellationToken ct)
    {
        db.Add(new CompanyCurrency { CompanyId = company, CurrencyId = next, PreviousCurrencyId = old, FromDate = first, Rate = rate, OwnerId = company, TenantId = request.TenantId });
        foreach (var currency in await db.Set<Currency>().Where(c => c.Active && c.OwnerId == company && (c.Id == old || c.Id == next)).ToListAsync(ct).ConfigureAwait(false))
        {
            currency.IsBase = currency.Id == next;
        }

        decimal At(decimal amount) => Currencies.CurrencyHistory.Scale(amount, 1 / rate);
        // What is still in stock is worth the same: its open cost layers in the new currency.
        foreach (var layer in await db.Set<ProductCostLayer>().Where(l => l.Active && l.CompanyId == company && l.QuantityLeft > 0).ToListAsync(ct).ConfigureAwait(false))
        {
            (layer.ValueLeftLcu, layer.UnitCostLcu) = (At(layer.ValueLeftLcu), layer.UnitCostLcu / rate);
        }

        // The price lists: their company-currency value, and the value itself where it is in the old currency (or in the company's, unnamed).
        foreach (var price in await db.Set<ProductPurchasePrice>().Where(p => p.Active && p.CompanyId == company).ToListAsync(ct).ConfigureAwait(false))
        {
            (price.ValueLcu, price.Value) = (price.ValueLcu is { } v ? At(v) : null, price.CurrencyId is null || price.CurrencyId == old ? (price.Value is { } w ? At(w) : null) : price.Value);
        }

        foreach (var price in await db.Set<ProductValuePrice>().Where(p => p.Active && p.CompanyId == company).ToListAsync(ct).ConfigureAwait(false))
        {
            (price.ValueLcu, price.Value) = (price.ValueLcu is { } v ? At(v) : null, price.CurrencyId is null || price.CurrencyId == old ? (price.Value is { } w ? At(w) : null) : price.Value);
        }

        foreach (var price in await db.Set<ProductSalePrice>().Where(p => p.Active && p.CompanyId == company).ToListAsync(ct).ConfigureAwait(false))
        {
            (price.ValueLcu, price.Value) = (price.ValueLcu is { } v ? At(v) : null, price.CurrencyId is null || price.CurrencyId == old ? (price.Value is { } w ? At(w) : null) : price.Value);
        }

        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        foreach (var price in await db.Set<ProductSalePriceType>().Where(p => p.Active && p.OwnerId != null && nodes.Contains(p.OwnerId.Value)).ToListAsync(ct).ConfigureAwait(false))
        {
            (price.ValueLcu, price.Value) = (price.ValueLcu is { } v ? At(v) : null, price.CurrencyId is null || price.CurrencyId == old ? (price.Value is { } w ? At(w) : null) : price.Value);
        }
    }
}

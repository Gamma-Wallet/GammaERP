using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Currencies;

/// <summary>
/// The company's own currency from a day on (the founder, 2026-11-04): set when a fiscal year is closed with a new currency, from the next
/// year's first day. What was posted before stays in the previous currency; a report read after the change shows it converted: divided by
/// <see cref="Rate"/>, the new currency's rate of that day in the previous one (1 EUR = 97.50 ALL), as exchange rates are entered.
/// </summary>
[Table("tbl_company_currency")]
public sealed class CompanyCurrency : Row
{
    public Guid CompanyId { get; set; }

    /// <summary>The currency the company keeps its books in from <see cref="FromDate"/>.</summary>
    public Guid CurrencyId { get; set; }

    /// <summary>The currency before.</summary>
    public Guid PreviousCurrencyId { get; set; }

    /// <summary>The first day in the new currency: a fiscal year's first day.</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>Previous units for one new unit: the new currency's exchange rate of the first day.</summary>
    public decimal Rate { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Currencies;

/// <summary>An exchange rate between two currencies.</summary>
[Table("tbl_exchange_rate")]
public sealed class ExchangeRate : Row
{
    public Guid? CurrencyId { get; set; }

    public Guid? Currency2Id { get; set; }

    public decimal? Rate { get; set; }

    /// <summary>The day the rate is for; a document takes the latest on or before its day.</summary>
    public DateOnly? RateDate { get; set; }
}

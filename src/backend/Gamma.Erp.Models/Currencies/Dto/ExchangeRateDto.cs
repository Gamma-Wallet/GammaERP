using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Currencies.Dto;

/// <summary>An exchange rate between two currencies.</summary>
public sealed record ExchangeRateDto : RowDto
{
    public Guid? CurrencyId { get; init; }

    public Guid? Currency2Id { get; init; }

    public decimal? Rate { get; init; }
}

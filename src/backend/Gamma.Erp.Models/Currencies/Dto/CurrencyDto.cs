using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Currencies.Dto;

/// <summary>A currency, e.g. ALL (the base), EUR, USD.</summary>
public sealed record CurrencyDto : RowDto
{
    public string? Code { get; init; }

    public Dictionary<string, string>? Name { get; init; }

    /// <summary>The company's own currency, the one amounts are kept in besides the document's.</summary>
    public bool IsBase { get; init; }
}

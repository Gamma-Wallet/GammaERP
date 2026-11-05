using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Payments.Dto;

public sealed record VatGroupDto : RowDto
{
    public string? Code { get; init; }

    public Dictionary<string, string>? Name { get; init; }

    public Dictionary<string, string>? Description { get; init; }

    /// <summary>Old rate.</summary>
    public decimal? Rate { get; init; }

    public bool Depreciation { get; init; }
}

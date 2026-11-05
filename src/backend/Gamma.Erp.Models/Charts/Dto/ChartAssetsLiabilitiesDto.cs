using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Charts.Dto;

public sealed record ChartAssetsLiabilitiesDto : RowDto
{
    public string? Code { get; init; }

    public Dictionary<string, string>? Name { get; init; }
}

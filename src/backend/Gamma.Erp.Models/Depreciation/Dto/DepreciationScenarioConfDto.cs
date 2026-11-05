using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Depreciation.Dto;

public sealed record DepreciationScenarioConfDto : RowDto
{
    public Guid? DepreciationScenarioId { get; init; }

    public Guid? ChartId { get; init; }

    public string? ChartName { get; init; }

    public bool Side { get; init; }

    public string? SideValue { get; init; }
}

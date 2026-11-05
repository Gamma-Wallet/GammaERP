using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Charts.Dto;

/// <summary>A category of accounts, as the screens see it.</summary>
public sealed record ChartCategoryDto : CategoryDto
{
    /// <summary>Whether this chart is the statutory one the scenarios post to.</summary>
    public bool IsStatutory { get; init; }
}

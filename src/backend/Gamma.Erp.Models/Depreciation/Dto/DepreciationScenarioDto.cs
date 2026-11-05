using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Depreciation.Dto;

public sealed record DepreciationScenarioDto : RowDto
{
    public Dictionary<string, string>? Name { get; init; }

    public Dictionary<string, string>? Description { get; init; }

    public Guid? DepreciationBookId { get; init; }

    public Guid? ElementsId { get; init; }

    /// <summary>The useful life in months it proposes for a new asset.</summary>
    public int? Months { get; init; }

    /// <summary>The linear percentage a year it proposes.</summary>
    public decimal? LinearPercentage { get; init; }

    /// <summary>The declining percentage a year it proposes.</summary>
    public decimal? DecliningPercentage { get; init; }
}

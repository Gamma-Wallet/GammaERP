using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Depreciation.Dto;

public sealed record DepreciationBookDto : RowDto
{
    public Dictionary<string, string>? Name { get; init; }

    public Dictionary<string, string>? Description { get; init; }

    public Guid? MethodId { get; init; }

    public Guid? ChartCategoryId { get; init; }

    public bool Posts { get; init; }
}

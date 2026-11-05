using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Depreciation.Dto;

public sealed record AssetDepreciationDto : RowDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    public Guid? AssetValueId { get; init; }

    public Guid? CostCenterId { get; init; }

    public DateOnly? DepreciationStartDate { get; init; }

    public DateOnly? DepreciationDate { get; init; }

    public decimal? DepreciationValue { get; init; }

    public decimal? BookValue { get; init; }

    public bool Posted { get; init; }

    public Guid? DocumentId { get; init; }

    public decimal? PostedValue { get; init; }
}

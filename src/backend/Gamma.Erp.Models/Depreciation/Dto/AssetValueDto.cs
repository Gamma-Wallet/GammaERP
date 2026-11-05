using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Depreciation.Dto;

public sealed record AssetValueDto : RowDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    public Guid? ProductSpecificUnitId { get; init; }

    public Guid? DocumentId { get; init; }

    public Guid? BookId { get; init; }

    public Guid? MethodId { get; init; }

    public DateOnly? StartDate { get; init; }

    public decimal? Years { get; init; }

    public decimal? Months { get; init; }

    public decimal? InitialValue { get; init; }
}

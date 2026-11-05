using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Depreciation.Dto;

public sealed record AssetValueUniqueDto : RowDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    public Guid? ProductSpecificUnitId { get; init; }

    public Guid? BookId { get; init; }

    public Guid? MethodId { get; init; }

    public DateOnly? EndDate { get; init; }

    public decimal? LinearPercentage { get; init; }

    public decimal? DecliningPercentage { get; init; }

    public DateOnly? DisposeDate { get; init; }

    public bool Disposed { get; init; }
}

using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Charts.Dto;

public sealed record ChartTotalValueDto : RowDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    public Guid? ChartId { get; init; }

    public Guid? CostCenterId { get; init; }

    public Guid? ChartCalcValueId { get; init; }

    public DateOnly? PostingDate { get; init; }

    public decimal? Value { get; init; }
}

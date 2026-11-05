using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Charts.Dto;

public sealed record ChartCalcValueDto : RowDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    public Guid? ChartId { get; init; }

    public Guid? CostCenterId { get; init; }

    public Guid? ChartValueId { get; init; }

    public Guid? DocumentId { get; init; }

    public Guid? TransactionId { get; init; }

    public DateOnly? PostingDate { get; init; }

    public decimal? Value { get; init; }

    /// <summary>The company's country, on the entries of a chart other than the statutory one.</summary>
    public string? Country { get; init; }
}

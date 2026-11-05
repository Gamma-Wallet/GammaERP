using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Charts.Dto;

/// <summary>A statutory account and the account of another chart it is posted on.</summary>
public sealed record InterChartChartDto : RowDto
{
    public Guid? Chart1Id { get; init; }

    public Guid? Chart2Id { get; init; }
}

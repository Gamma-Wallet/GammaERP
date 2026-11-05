using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Products.Dto;

public sealed record InterProductCategoryTypeChartDto : RowDto
{
    public Guid? InterProductTypeCategoryId { get; init; }

    public Guid? ChartId { get; init; }
}

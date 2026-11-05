using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Products.Dto;

public sealed record InterCostCenterProductStockDto : RowDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    public Guid? CostCenterId { get; init; }

    public Guid? DocumentId { get; init; }

    public Guid? ProductSpecificUnitId { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? DocumentQuantity { get; init; }
}

using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Products.Dto;

public sealed record InterPartnerProductStockDto : RowDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    public Guid? PartnerId { get; init; }

    public Guid? DocumentId { get; init; }

    public Guid? ProductSpecificUnitId { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? DocumentQuantity { get; init; }
}

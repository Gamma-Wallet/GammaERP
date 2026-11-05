using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Products.Dto;

public sealed record ProductSalePriceDto : RowDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    public Guid? ProductId { get; init; }

    public Guid? ProductSpecificUnitId { get; init; }

    public Guid? PartnerCategoryId { get; init; }

    public Guid? PartnerTypeId { get; init; }

    public Guid? CurrencyId { get; init; }

    public decimal? Value { get; init; }

    public decimal? ValueLcu { get; init; }
}

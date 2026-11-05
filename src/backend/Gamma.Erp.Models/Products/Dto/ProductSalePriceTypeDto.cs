using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Products.Dto;

public sealed record ProductSalePriceTypeDto : RowDto
{
    public Guid? ProductTypeUnitId { get; init; }

    public Guid? PartnerCategoryId { get; init; }

    public Guid? PartnerTypeId { get; init; }

    public Guid? CurrencyId { get; init; }

    public decimal? Value { get; init; }

    public decimal? ValueLcu { get; init; }
}

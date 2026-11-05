using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

[Table("tbl_product_sale_price_type")]
public sealed class ProductSalePriceType : Row
{
    public Guid? ProductTypeUnitId { get; set; }

    public Guid? PartnerCategoryId { get; set; }

    public Guid? PartnerTypeId { get; set; }

    public Guid? CurrencyId { get; set; }

    public decimal? Value { get; set; }

    public decimal? ValueLcu { get; set; }
}

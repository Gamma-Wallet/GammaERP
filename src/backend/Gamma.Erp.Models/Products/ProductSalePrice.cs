using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

[Table("tbl_product_sale_price")]
public sealed class ProductSalePrice : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? ProductSpecificUnitId { get; set; }

    public Guid? PartnerCategoryId { get; set; }

    public Guid? PartnerTypeId { get; set; }

    public Guid? CurrencyId { get; set; }

    public decimal? Value { get; set; }

    public decimal? ValueLcu { get; set; }
}

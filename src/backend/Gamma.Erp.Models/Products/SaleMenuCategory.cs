using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>A product category a sale menu shows, in its order on the menu.</summary>
[Table("tbl_sale_menu_category")]
public sealed class SaleMenuCategory : Row
{
    public Guid SaleMenuId { get; set; }

    public Guid ProductCategoryId { get; set; }

    public double SortOrder { get; set; }
}

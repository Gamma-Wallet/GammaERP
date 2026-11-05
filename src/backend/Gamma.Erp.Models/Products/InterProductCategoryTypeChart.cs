using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

[Table("tbl_inter_product_category_type_chart")]
public sealed class InterProductCategoryTypeChart : Row
{
    public Guid? InterProductTypeCategoryId { get; set; }

    public Guid? ChartId { get; set; }
}

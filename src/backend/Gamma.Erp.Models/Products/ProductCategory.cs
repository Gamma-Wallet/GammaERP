using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>A category of products.</summary>
[Table("tbl_product_category")]
public sealed class ProductCategory : Category
{
    public bool Pk { get; set; }
}

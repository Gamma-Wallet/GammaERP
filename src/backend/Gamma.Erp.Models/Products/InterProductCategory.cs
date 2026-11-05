using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>A product in a product category.</summary>
[Table("tbl_inter_product_category")]
public sealed class InterProductCategory : InterElementCategory;

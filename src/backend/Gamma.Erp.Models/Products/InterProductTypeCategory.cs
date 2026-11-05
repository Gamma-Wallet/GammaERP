using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>A product type in a product category.</summary>
[Table("tbl_inter_product_type_category")]
public sealed class InterProductTypeCategory : InterTypeCategory;

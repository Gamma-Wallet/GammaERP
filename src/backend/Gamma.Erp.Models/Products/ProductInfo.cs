using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>The value of a property for a product.</summary>
[Table("tbl_product_info")]
public sealed class ProductInfo : ElementInfo;

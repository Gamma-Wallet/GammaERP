using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>A property of a product type.</summary>
[Table("tbl_product_type_info")]
public sealed class ProductTypeInfo : TypeInfo;

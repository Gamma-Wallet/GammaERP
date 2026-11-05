using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>A choice of a product list property.</summary>
[Table("tbl_product_type_info_ch")]
public sealed class ProductTypeInfoChoice : TypeInfoChoice;

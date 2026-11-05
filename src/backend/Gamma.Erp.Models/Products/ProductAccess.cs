using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>A user who may see a product.</summary>
[Table("tbl_product_access")]
public sealed class ProductAccess : ElementAccess;

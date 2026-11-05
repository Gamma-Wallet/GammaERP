using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>A product.</summary>
[Table("tbl_product")]
public sealed class Product : Element
{
    /// <summary>The VAT group its lines start with, over its type's.</summary>
    public Guid? VatGroupId { get; set; }
}

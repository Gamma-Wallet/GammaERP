using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>A batch of a product that tracks lots (the founder, 2026-11-02). It says which batch is where; cost stays per product.</summary>
[Table("tbl_product_lot")]
public sealed class ProductLot : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>The lot number, once per product in a company.</summary>
    public string Lot { get; set; } = string.Empty;

    public DateOnly? ExpiryDate { get; set; }
}

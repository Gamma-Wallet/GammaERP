using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>How much of a lot, in one unit, one holder has: a place, a member of staff or a client.</summary>
[Table("tbl_product_lot_stock")]
public sealed class ProductLotStock : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid ProductLotId { get; set; }

    public Guid UnitId { get; set; }

    /// <summary>The table of who holds it.</summary>
    public string? HolderTable { get; set; }

    public Guid HolderId { get; set; }

    public decimal Quantity { get; set; }
}

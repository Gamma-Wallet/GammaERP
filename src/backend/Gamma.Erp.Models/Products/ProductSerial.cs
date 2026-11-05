using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>
/// One unit of a product that tracks serial numbers, and who holds it now (a register under the product, the founder, 2026-11-01). One unit in one place: posting moves it.
/// </summary>
[Table("tbl_product_serial")]
public sealed class ProductSerial : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>The serial number, once per product in a company.</summary>
    public string Serial { get; set; } = string.Empty;

    public string? PartNumber { get; set; }

    /// <summary>The table of who holds it: a cost centre, a partner or a member of staff.</summary>
    public string? HolderTable { get; set; }

    /// <summary>Who holds it; empty once it has left with nobody to hold it.</summary>
    public Guid? HolderId { get; set; }
}

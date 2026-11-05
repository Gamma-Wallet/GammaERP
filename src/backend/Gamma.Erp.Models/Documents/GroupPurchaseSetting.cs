using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>
/// What a sale from the group makes at the company it is sold to (the founder, 2026-10-31): each book's row pairs a sale type with the book's
/// purchase type. The group's row says which of its sales make a purchase; the company's row with the same code says which document type that
/// purchase is and where the goods arrive. Document type codes differ per country, so the row has its own. A book without the row takes part in nothing.
/// </summary>
[Table("tbl_group_purchase_setting")]
public sealed class GroupPurchaseSetting : Row
{
    /// <summary>Pairs the group's row with the company's.</summary>
    public string? Code { get; set; }

    /// <summary>The sale document type of this book, used when this book is the group selling.</summary>
    public Guid? SaleTypeId { get; set; }

    /// <summary>The purchase document type of this book, used when this book is the company bought for.</summary>
    public Guid? PurchaseTypeId { get; set; }

    /// <summary>The cost centre the goods arrive at; empty: the company chooses it on the draft.</summary>
    public Guid? WarehouseId { get; set; }
}

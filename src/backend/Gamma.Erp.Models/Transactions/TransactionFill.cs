using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

/// <summary>
/// How a line of a document type × transaction type is filled in: for each field 0 = typed (manual), 1 = chosen from the database, 2 = not needed. With the line's
/// default quantity and discount.
/// </summary>
[Table("tbl_transaction_fill")]
public sealed class TransactionFill : Row
{
    public Guid? DocumentTypeTransactionTypeId { get; set; }

    public int? TransactionType { get; set; }

    public int? VatGroup { get; set; }

    public int? Element { get; set; }

    public int? Unit { get; set; }

    public int? Location { get; set; }

    public int? UnitPriceWithoutVat { get; set; }

    public int? Discount { get; set; }

    public int? Quantity { get; set; }

    public int? PriceWithoutVat { get; set; }

    public int? Vat { get; set; }

    /// <summary>The quantity a line gets when none is given.</summary>
    public decimal? DefaultQuantity { get; set; }

    /// <summary>The discount per unit a line gets when none is given.</summary>
    public decimal? DefaultDiscount { get; set; }
}

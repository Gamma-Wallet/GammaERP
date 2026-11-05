using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>
/// How a point of sale works (B13, the founder's approval 2026-10-31): which document type is its bill, which ones take money in and pay money
/// out, which cash desk it pays into, which client a walk-in bill is written to and how it is paid by default. A row with no point of sale is
/// the company's default, or with a kind (the founder, 2026-10-31) that kind's; a point of sale's own row overrides its kind's and that the
/// default's, column by column (an empty column takes the next one's).
/// </summary>
[Table("tbl_point_of_sale_setting")]
public sealed class PointOfSaleSetting : Row
{
    /// <summary>The point of sale (a cost centre) these settings are for; empty: the company's default.</summary>
    public Guid? PointOfSaleId { get; set; }

    /// <summary>The kind of point of sale (a cost-centre type) these settings are for, when not for one point of sale.</summary>
    public Guid? PointOfSaleTypeId { get; set; }

    /// <summary>The screen it opens: tables, shop, events or stays.</summary>
    public string? Screen { get; set; }

    /// <summary>The document type of a bill (posted when the order is sent; stock leaves the point of sale).</summary>
    public Guid? BillTypeId { get; set; }

    /// <summary>The document type a waiter sends as an order, with the bill made later at payment; empty: the bill itself is sent.</summary>
    public Guid? OrderTypeId { get; set; }

    /// <summary>The document type that takes a bill's money into the cash desk.</summary>
    public Guid? MoneyInTypeId { get; set; }

    /// <summary>The document type that pays money out of the cash desk.</summary>
    public Guid? MoneyOutTypeId { get; set; }

    /// <summary>The cash desk (a cost centre) the money goes into and out of.</summary>
    public Guid? CashDeskId { get; set; }

    /// <summary>The client a bill is written to when the guest is not known (a partner).</summary>
    public Guid? WalkInClientId { get; set; }

    /// <summary>What a night is sold as, at a hotel.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>How a bill is paid unless the waiter chooses otherwise.</summary>
    public Guid? PaymentMethodId { get; set; }

    /// <summary>Whether bills need an open shift; empty: the next setting's, and in the end they do.</summary>
    public bool? RequiresShift { get; set; }
}

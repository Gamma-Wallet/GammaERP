using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>
/// A place held (the founder, 2026-10-31): a seat at a show, paid at booking, or a room from one day to another, open until check-out. The
/// database refuses a second reservation of the same seat at a show (a unique index) and of the same room on overlapping days (an exclusion
/// constraint), so two tills can't sell the same place. A cancelled one is switched off.
/// </summary>
[Table("tbl_pos_reservation")]
public sealed class PosReservation : Row
{
    /// <summary>The point of sale it was made at.</summary>
    public Guid PointOfSaleId { get; set; }

    /// <summary>The seat or the room (a cost centre).</summary>
    public Guid PlaceId { get; set; }

    /// <summary>The show a seat is held at; empty for a room.</summary>
    public Guid? ShowId { get; set; }

    public string? GuestName { get; set; }

    /// <summary>A room's first night.</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>The day a room is left; it is free again that night.</summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>A room's price per night without VAT; empty: the product's sale price.</summary>
    public decimal? Price { get; set; }

    /// <summary>The bill: a seat's when it is booked, a room's at check-out.</summary>
    public Guid? BillId { get; set; }

    /// <summary>The day the guest arrived (checked in by hand).</summary>
    public DateOnly? CheckedInOn { get; set; }
}

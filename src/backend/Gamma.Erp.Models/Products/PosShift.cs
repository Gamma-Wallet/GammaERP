using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>A shift of a point of sale, opened and closed by hand (the founder, 2026-11-02). Closed is final; a difference is recorded here and posts nothing.</summary>
[Table("tbl_pos_shift")]
public sealed class PosShift : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid PointOfSaleId { get; set; }

    public Guid CashDeskId { get; set; }

    public Guid? OpenedBy { get; set; }

    public DateTimeOffset OpenedOn { get; set; }

    /// <summary>The cash in the drawer at the start.</summary>
    public decimal OpeningCash { get; set; }

    public Guid? ClosedBy { get; set; }

    public DateTimeOffset? ClosedOn { get; set; }

    /// <summary>What should be in the drawer: worked out when the shift closes, after the count.</summary>
    public decimal? ExpectedCash { get; set; }

    /// <summary>What the cashier counted.</summary>
    public decimal? CountedCash { get; set; }

    /// <summary>Counted minus expected.</summary>
    public decimal? Difference { get; set; }

    public string? Note { get; set; }
}

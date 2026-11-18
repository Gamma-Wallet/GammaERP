using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// A numbering series of declared bills at a point of sale or till (plan 4.3c): its code, the authority's document type, the country's device / unit /
/// validation codes, and where the chain stands. The number and the chain move only when the authority confirms, in the same save as the receipt.
/// </summary>
[Table("tbl_fiscal_series")]
public sealed class FiscalSeries : Row
{
    public Guid FiscalProviderId { get; set; }

    /// <summary>The point of sale or till whose bills it numbers.</summary>
    public Guid CostCenterId { get; set; }

    public string SeriesCode { get; set; } = string.Empty;

    /// <summary>The authority's document type of these bills (e.g. myDATA 11.1, retail receipt).</summary>
    public string DocumentCode { get; set; } = string.Empty;

    /// <summary>Albania: the TCR (device) code.</summary>
    public string? DeviceCode { get; set; }

    /// <summary>Albania: the business unit code.</summary>
    public string? UnitCode { get; set; }

    /// <summary>Portugal: the series' ATCUD validation code.</summary>
    public string? ValidationCode { get; set; }

    public int LastNumber { get; set; }

    /// <summary>Spain and Portugal: the last confirmed record's hash (the chain).</summary>
    public string? LastHash { get; set; }

    public DateTimeOffset? LastIssuedOn { get; set; }
}

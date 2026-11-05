using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

/// <summary>
/// A card or chart of the dashboard: what it shows comes from here, per company.
/// The engine knows only how to work out each kind (Total, Balance, Drafts, Monthly, MonthlyVat, TopPartners, TopProducts, ByCostCenter).
/// </summary>
[Table("tbl_dashboard_item")]
public sealed class DashboardItem : Row
{
    public string? Code { get; set; }

    public TranslatedText? Name { get; set; }

    /// <summary>How it is worked out.</summary>
    public string? Kind { get; set; }

    /// <summary>The documents it counts. On a balance, the documents its report opens with.</summary>
    public Guid? DocumentCategoryId { get; set; }

    /// <summary>The documents a chart sets against the first, or that a count adds to them.</summary>
    public Guid? SecondCategoryId { get; set; }

    /// <summary>The partners of a balance.</summary>
    public Guid? PartnerCategoryId { get; set; }

    /// <summary>The other way round: a balance shows with the other sign (what is owed to suppliers stands on the credit side), a total counts a rise as bad.</summary>
    public bool Negate { get; set; }

    public double SortOrder { get; set; }
}

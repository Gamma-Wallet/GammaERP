using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

/// <summary>A category of account of the charts.</summary>
[Table("tbl_chart_category")]
public sealed class ChartCategory : Category
{
    /// <summary>
    /// Whether this chart is the company's statutory one (the state's, e.g. Albania's): the chart its scenarios post to. Other charts (the
    /// corporate's, IFRS…) get their entries through the account mapping (tbl_inter_chart_chart). New (the founder's design, 2026-10-29).
    /// </summary>
    public bool IsStatutory { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

/// <summary>
/// A company's account mapping between two charts: its statutory account (chart1) is posted again on an account of another chart (chart2),
/// e.g. the corporate's chart or an IFRS chart. Many statutory accounts may map to one. New (the founder's design, 2026-10-29).
/// </summary>
[Table("tbl_inter_chart_chart")]
public sealed class InterChartChart : Row
{
    /// <summary>The statutory account.</summary>
    public Guid? Chart1Id { get; set; }

    /// <summary>The account of the other chart it is posted on.</summary>
    public Guid? Chart2Id { get; set; }
}

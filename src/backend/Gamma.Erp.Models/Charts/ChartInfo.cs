using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

/// <summary>The value of a property for a account of the chart.</summary>
[Table("tbl_chart_info")]
public sealed class ChartInfo : ElementInfo;

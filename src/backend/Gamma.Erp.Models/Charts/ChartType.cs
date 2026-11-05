using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

/// <summary>A type of account of the chart.</summary>
[Table("tbl_chart_type")]
public sealed class ChartType : ElementType;

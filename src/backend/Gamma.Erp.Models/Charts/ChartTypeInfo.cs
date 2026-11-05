using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

/// <summary>A property of a account of the chart type.</summary>
[Table("tbl_chart_type_info")]
public sealed class ChartTypeInfo : TypeInfo;

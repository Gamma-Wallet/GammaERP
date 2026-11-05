using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

/// <summary>A choice of a account of the chart list property.</summary>
[Table("tbl_chart_type_info_ch")]
public sealed class ChartTypeInfoChoice : TypeInfoChoice;

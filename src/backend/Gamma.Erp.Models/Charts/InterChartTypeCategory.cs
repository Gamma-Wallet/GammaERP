using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

/// <summary>A account of the chart type in a account of the chart category.</summary>
[Table("tbl_inter_chart_type_category")]
public sealed class InterChartTypeCategory : InterTypeCategory;

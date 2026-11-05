using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

/// <summary>A account of the chart in a account of the chart category.</summary>
[Table("tbl_inter_chart_category")]
public sealed class InterChartCategory : InterElementCategory;

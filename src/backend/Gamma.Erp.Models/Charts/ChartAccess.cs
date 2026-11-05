using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

/// <summary>A user who may see a account of the chart.</summary>
[Table("tbl_chart_access")]
public sealed class ChartAccess : ElementAccess;

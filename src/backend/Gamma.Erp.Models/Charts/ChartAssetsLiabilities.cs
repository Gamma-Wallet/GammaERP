using System.ComponentModel.DataAnnotations.Schema;

namespace Gamma.Erp.Models.Charts;

/// <summary>A line of the balance sheet.</summary>
[Table("tbl_chart_assets_liabilities")]
public sealed class ChartAssetsLiabilities : StatementLine;

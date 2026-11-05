using System.ComponentModel.DataAnnotations.Schema;

namespace Gamma.Erp.Models.Charts;

/// <summary>A line of the profit and loss.</summary>
[Table("tbl_chart_profit_loss")]
public sealed class ChartProfitLoss : StatementLine;

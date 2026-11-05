using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

[Table("tbl_chart_total_value")]
public sealed class ChartTotalValue : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid? ChartId { get; set; }

    public Guid? CostCenterId { get; set; }

    public Guid? ChartCalcValueId { get; set; }

    public DateOnly? PostingDate { get; set; }

    public decimal? Value { get; set; }
}

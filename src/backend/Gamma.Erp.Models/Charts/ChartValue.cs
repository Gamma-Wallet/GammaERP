using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

[Table("tbl_chart_value")]
public sealed class ChartValue : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid? ChartId { get; set; }

    public Guid? CostCenterId { get; set; }

    public Guid? DocumentId { get; set; }

    public Guid? TransactionId { get; set; }

    public DateOnly? PostingDate { get; set; }

    public decimal? Debit { get; set; }

    public decimal? Credit { get; set; }

    /// <summary>The company's country (ISO code), on the entries of a chart other than the statutory one (the corporate's), so it reads per country and company.</summary>
    public string? Country { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A statement for one bank account: a file uploaded by hand, or lines fetched from the bank through its connection.</summary>
[Table("tbl_bank_statement")]
public sealed class BankStatement : Row
{
    /// <summary>The bank: a cost centre.</summary>
    public Guid BankId { get; set; }

    /// <summary>The file format it was read with; empty when it was fetched from the bank.</summary>
    public Guid? FormatId { get; set; }

    public string? FileName { get; set; }

    /// <summary>The bank connection it was fetched through; empty for an uploaded file.</summary>
    public Guid? ConnectionId { get; set; }

    /// <summary>The days fetched.</summary>
    public DateOnly? PeriodFrom { get; set; }

    public DateOnly? PeriodTo { get; set; }

    /// <summary>The bank's booked balance when it was fetched, shown next to the ledger's.</summary>
    public decimal? BankBalance { get; set; }

    public DateOnly? BankBalanceDate { get; set; }
}

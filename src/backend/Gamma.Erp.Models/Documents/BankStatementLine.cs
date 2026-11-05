using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>One line of a bank statement; a person matches it to a document or ignores it.</summary>
[Table("tbl_bank_statement_line")]
public sealed class BankStatementLine : Row
{
    public Guid StatementId { get; set; }

    public DateOnly LineDate { get; set; }

    public string? Text { get; set; }

    public string? Reference { get; set; }

    /// <summary>Money in positive, money out negative.</summary>
    public decimal Amount { get; set; }

    /// <summary>The posted document this line was matched to.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>Set aside by hand, e.g. a line that needs no document.</summary>
    public bool Ignored { get; set; }
}

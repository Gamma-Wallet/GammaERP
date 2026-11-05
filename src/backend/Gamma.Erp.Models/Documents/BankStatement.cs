using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A statement file uploaded for one bank account.</summary>
[Table("tbl_bank_statement")]
public sealed class BankStatement : Row
{
    /// <summary>The bank: a cost centre.</summary>
    public Guid BankId { get; set; }

    public Guid FormatId { get; set; }

    public string? FileName { get; set; }
}

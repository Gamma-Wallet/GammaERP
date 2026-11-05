using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>How a contract is billed: every so many months from its next date. Stopping it is switching the row off.</summary>
[Table("tbl_contract_billing")]
public sealed class ContractBilling : Row
{
    /// <summary>The contract, a document.</summary>
    public Guid ContractId { get; set; }

    /// <summary>The months between two invoices.</summary>
    public int EveryMonths { get; set; }

    /// <summary>The day the next invoice is due; moved on when one is made, so a period is never billed twice.</summary>
    public DateOnly NextDate { get; set; }
}

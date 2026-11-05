using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>Up to what total a role's documents pass without approval, and what it may approve. A company with no such rows approves nothing. New here.</summary>
[Table("tbl_approval_limit")]
public sealed class ApprovalLimit : Row
{
    /// <summary>The role.</summary>
    public Guid RoleId { get; set; }

    /// <summary>The largest total, with VAT, in the company's currency.</summary>
    public decimal Limit { get; set; }
}

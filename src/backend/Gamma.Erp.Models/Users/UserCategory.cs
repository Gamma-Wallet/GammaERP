using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>A category of users.</summary>
[Table("tbl_user_category")]
public sealed class UserCategory : Category
{
    /// <summary>
    /// Whether the members of this role are administrators: every right, still only at their node and below.
    /// </summary>
    public bool IsAdministrator { get; set; }
}

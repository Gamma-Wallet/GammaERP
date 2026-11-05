using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>A menu node a user, or a role, may see, with everything under it.</summary>
[Table("tbl_inter_menu_user")]
public sealed class InterMenuUser : Row
{
    public Guid? MenuId { get; set; }

    /// <summary>The user; null when the row is for a role.</summary>
    public Guid? UserId { get; set; }

    /// <summary>The role (a user category); null when the row is for a user.</summary>
    public Guid? RoleId { get; set; }
}

namespace Gamma.Erp.Models.Base;

/// <summary>
/// Who may see an element: one user, or every user of a role. A role is a user category (ADMINISTRATOR, FINANCE,
/// SELLER, WAREHOUSE…), so a user can hold several. Exactly one of <see cref="UserId"/> and <see cref="RoleId"/> is set.
/// </summary>
public abstract class ElementAccess : Row
{
    public Guid ElementId { get; set; }

    /// <summary>The user who has the access.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Or the role (a user category, tbl_user_category) whose users have it.</summary>
    public Guid? RoleId { get; set; }
}

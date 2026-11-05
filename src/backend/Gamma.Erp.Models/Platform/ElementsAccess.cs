using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

/// <summary>
/// What a user, or every user of a role, may do with an entity or one of its categories: read all its
/// elements, read only their own, write, forward. Exactly one of <see cref="UserId"/> and <see cref="RoleId"/> is set.
/// </summary>
[Table("tbl_elements_access")]
public sealed class ElementsAccess : Row
{
    /// <summary>The user who has the rights.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Or the role (a user category, tbl_user_category) whose users have them.</summary>
    public Guid? RoleId { get; set; }

    /// <summary>The entity.</summary>
    public Guid? ElementsId { get; set; }

    /// <summary>Only this category of the entity; null = the whole entity.</summary>
    public Guid? CategoryId { get; set; }

    public bool Write { get; set; }

    public bool Forward { get; set; }

    /// <summary>Read every element.</summary>
    public bool ReadAll { get; set; }

    /// <summary>Read only the elements given to the user or to one of the user's roles on the row's Access tab.</summary>
    public bool ReadOwn { get; set; }
}

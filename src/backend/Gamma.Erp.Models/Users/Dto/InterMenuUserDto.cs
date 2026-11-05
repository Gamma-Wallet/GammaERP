using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Users.Dto;

/// <summary>A menu node a user, or a role, may see.</summary>
public sealed record InterMenuUserDto : RowDto
{
    public Guid? MenuId { get; init; }

    /// <summary>The user; null when the row is for a role.</summary>
    public Guid? UserId { get; init; }

    /// <summary>The role; null when the row is for a user.</summary>
    public Guid? RoleId { get; init; }
}

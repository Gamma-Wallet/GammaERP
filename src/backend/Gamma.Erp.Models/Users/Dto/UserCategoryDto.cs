using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Users.Dto;

/// <summary>A category of users, as the screens see it.</summary>
public sealed record UserCategoryDto : CategoryDto
{
    /// <summary>Whether the members of this role are administrators.</summary>
    public bool IsAdministrator { get; init; }
}

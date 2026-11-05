using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Users.Dto;

/// <summary>A user, with its categories and property values, as the screens see it.</summary>
public sealed record UserDto : ElementDto
{
    public string? Username { get; init; }
}

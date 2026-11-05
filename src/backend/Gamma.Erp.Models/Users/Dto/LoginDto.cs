using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Users.Dto;

public sealed record LoginDto : RowDto
{
    /// <summary>The user who signed in or out.</summary>
    public Guid UserId { get; init; }

    public bool InOut { get; init; }
}

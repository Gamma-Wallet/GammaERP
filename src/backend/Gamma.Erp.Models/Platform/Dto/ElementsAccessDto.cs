using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Platform.Dto;

public sealed record ElementsAccessDto : RowDto
{
    /// <summary>The user who has the rights; or <see cref="RoleId"/>.</summary>
    public Guid? UserId { get; init; }

    /// <summary>The role (a user category) whose users have them.</summary>
    public Guid? RoleId { get; init; }

    /// <summary>The entity (tbl_elements).</summary>
    public Guid? ElementsId { get; init; }

    public Guid? CategoryId { get; init; }

    public bool Write { get; init; }

    public bool Forward { get; init; }

    public bool ReadAll { get; init; }

    public bool ReadOwn { get; init; }
}

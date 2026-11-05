using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Documents.Dto;

public sealed record DocumentWorkingDto : RowDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    public Guid? DocumentId { get; init; }

    /// <summary>The user holding it.</summary>
    public Guid? UserId { get; init; }

    public bool Working { get; init; }
}

using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Documents.Dto;

/// <summary>A document type given to a user or to a role.</summary>
public sealed record InterDocumentTypeUserDto : RowDto
{
    public Guid? DocumentTypeId { get; init; }

    public Guid? UserId { get; init; }

    public Guid? RoleId { get; init; }
}

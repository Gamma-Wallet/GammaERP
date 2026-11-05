using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Documents.Dto;

/// <summary>A document created from another: document2 was made from document1.</summary>
public sealed record InterDocumentDocumentDto : RowDto
{
    public Guid? Document1Id { get; init; }

    public Guid? Document2Id { get; init; }
}

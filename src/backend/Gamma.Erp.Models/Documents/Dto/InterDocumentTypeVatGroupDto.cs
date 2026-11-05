using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Documents.Dto;

public sealed record InterDocumentTypeVatGroupDto : RowDto
{
    public Guid? DocumentTypeId { get; init; }

    public Guid? VatGroupId { get; init; }
}

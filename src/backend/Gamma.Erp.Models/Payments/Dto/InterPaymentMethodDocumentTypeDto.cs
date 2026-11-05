using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Payments.Dto;

public sealed record InterPaymentMethodDocumentTypeDto : RowDto
{
    public Guid? PaymentMethodId { get; init; }

    public Guid? DocumentTypeId { get; init; }
}

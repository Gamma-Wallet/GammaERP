using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Transactions.Dto;

public sealed record InterDocumentTypeTransactionTypePaymentMethodDto : RowDto
{
    public Guid? PaymentMethodDocumentTypeId { get; init; }

    public Guid? TransactionTypeId { get; init; }
}

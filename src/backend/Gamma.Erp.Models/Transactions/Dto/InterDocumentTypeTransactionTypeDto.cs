using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Transactions.Dto;

public sealed record InterDocumentTypeTransactionTypeDto : RowDto
{
    public Guid? DocumentTypeId { get; init; }

    public Guid? TransactionTypeId { get; init; }
}

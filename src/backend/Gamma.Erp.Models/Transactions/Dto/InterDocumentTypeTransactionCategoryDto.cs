using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Transactions.Dto;

public sealed record InterDocumentTypeTransactionCategoryDto : RowDto
{
    public Guid? DocumentTypeId { get; init; }

    public Guid? TransactionCategoryId { get; init; }
}

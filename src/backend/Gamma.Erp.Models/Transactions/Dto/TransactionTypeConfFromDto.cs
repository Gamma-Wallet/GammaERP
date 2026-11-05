using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Transactions.Dto;

public sealed record TransactionTypeConfFromDto : RowDto
{
    public Guid? DocumentTypeTransactionTypeId { get; init; }

    public Guid? ElementsId { get; init; }
}

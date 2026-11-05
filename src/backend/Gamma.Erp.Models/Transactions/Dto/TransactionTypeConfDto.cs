using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Transactions.Dto;

public sealed record TransactionTypeConfDto : RowDto
{
    public Guid? TransactionTypeId { get; init; }

    public Guid? ElementsId { get; init; }

    /// <summary>Whether the lines move stock: 1 from side down / to side up, 0 none.</summary>
    public int StockDirection { get; init; }
}

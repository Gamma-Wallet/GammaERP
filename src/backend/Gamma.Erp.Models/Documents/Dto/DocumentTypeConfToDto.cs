using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Documents.Dto;

public sealed record DocumentTypeConfToDto : RowDto
{
    public Guid? DocumentTypeId { get; init; }

    public Guid? ElementsId { get; init; }

    /// <summary>The amount that goes into this side's balance (a "Transaction amount" row), or none.</summary>
    public Guid? BalanceValueElementsId { get; init; }

    /// <summary>The amount that goes into the product's value for product lines on this side, or none.</summary>
    public Guid? ProductValueElementsId { get; init; }

    /// <summary>What posting does to the cost layers of the stock this side moves: 0 none, 1 take, 2 make, 3 receive, 4 put back, 5 take off, 6 make from what was used, 7 receive converted.</summary>
    public int CostLayers { get; init; }
}

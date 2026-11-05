using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Transactions.Dto;

public sealed record TransactionFillDto : RowDto
{
    public Guid? DocumentTypeTransactionTypeId { get; init; }

    public int? TransactionType { get; init; }

    public int? VatGroup { get; init; }

    public int? Element { get; init; }

    public int? Unit { get; init; }

    public int? Location { get; init; }

    public int? UnitPriceWithoutVat { get; init; }

    public int? Discount { get; init; }

    public int? Quantity { get; init; }

    public int? PriceWithoutVat { get; init; }

    public int? Vat { get; init; }

    /// <summary>The quantity a line gets when none is given.</summary>
    public decimal? DefaultQuantity { get; init; }

    /// <summary>The discount per unit a line gets when none is given.</summary>
    public decimal? DefaultDiscount { get; init; }
}

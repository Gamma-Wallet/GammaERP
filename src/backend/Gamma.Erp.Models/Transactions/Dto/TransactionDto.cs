using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Transactions.Dto;

/// <summary>A serial number on a line.</summary>
public sealed record TransactionSerialDto(string Serial, string? PartNumber = null);

/// <summary>A transaction, with its categories and property values, as the screens see it.</summary>
public sealed record TransactionDto : ElementDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    /// <summary>The serial numbers of a line whose product tracks them, one per unit.</summary>
    public IReadOnlyList<TransactionSerialDto> Serials { get; init; } = [];

    /// <summary>Whether its product tracks serial numbers.</summary>
    public bool TracksSerial { get; init; }

    public Guid? TypeConfId { get; init; }

    public Guid? DocumentId { get; init; }

    /// <summary>Where it comes from: a weak link whose entity the document type's configuration says.</summary>
    public Guid? FromId { get; init; }

    /// <summary>The name it had when the row was made.</summary>
    public string? FromName { get; init; }

    /// <summary>Where it goes to: a weak link whose entity the document type's configuration says.</summary>
    public Guid? ToId { get; init; }

    /// <summary>The name it had when the row was made.</summary>
    public string? ToName { get; init; }

    /// <summary>What it moves: the item or service from products, a weak link.</summary>
    public Guid? ElementId { get; init; }

    /// <summary>The name it had when the row was made.</summary>
    public string? ElementName { get; init; }

    public string? CurrencyName { get; init; }

    public Guid? CurrencyId { get; init; }

    public Guid? VatGroupId { get; init; }

    public Guid? UnitId { get; init; }

    public string? UnitName { get; init; }

    public decimal? Price { get; init; }

    public decimal? Discount { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? Value { get; init; }

    public decimal? Vat { get; init; }

    public decimal? ValueVat { get; init; }

    public decimal? ExchangeRate { get; init; }

    public decimal? DiscountLcu { get; init; }

    public decimal? ValueLcu { get; init; }

    public decimal? VatLcu { get; init; }

    public decimal? ValueVatLcu { get; init; }

    /// <summary>What the stock the line moved cost (first in, first out), set on posting.</summary>
    public decimal? CostLcu { get; init; }

    public bool Posted { get; init; }

    public Guid? ElementTypeId { get; init; }

    public decimal? Debit { get; init; }

    public decimal? Credit { get; init; }

    /// <summary>A journal line's own document number.</summary>
    public string? ReferenceNumber { get; init; }

    /// <summary>The lot of a product that tracks lots.</summary>
    public string? Lot { get; init; }

    public DateOnly? LotExpiryDate { get; init; }

    /// <summary>Whether the product tracks lots (read only).</summary>
    public bool TracksLot { get; init; }

    /// <summary>The type of the document a journal line stands for.</summary>
    public Guid? ReferenceDocumentTypeId { get; init; }
}

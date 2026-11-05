using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

/// <summary>A transaction.</summary>
[Table("tbl_transaction")]
public sealed class Transaction : Element
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid? TypeConfId { get; set; }

    public Guid? DocumentId { get; set; }

    /// <summary>Where it comes from: a weak link whose entity the document type's configuration says.</summary>
    public Guid? FromId { get; set; }

    /// <summary>The name it had when the row was made.</summary>
    public string? FromName { get; set; }

    /// <summary>Where it goes to: a weak link whose entity the document type's configuration says.</summary>
    public Guid? ToId { get; set; }

    /// <summary>The name it had when the row was made.</summary>
    public string? ToName { get; set; }

    /// <summary>What it moves: the item or service from products, a weak link.</summary>
    public Guid? ElementId { get; set; }

    /// <summary>The name it had when the row was made.</summary>
    public string? ElementName { get; set; }

    public string? CurrencyName { get; set; }

    public Guid? CurrencyId { get; set; }

    public Guid? VatGroupId { get; set; }

    public Guid? UnitId { get; set; }

    public string? UnitName { get; set; }

    public decimal? Price { get; set; }

    public decimal? Discount { get; set; }

    public decimal? Quantity { get; set; }

    public decimal? Value { get; set; }

    public decimal? Vat { get; set; }

    public decimal? ValueVat { get; set; }

    public decimal? ExchangeRate { get; set; }

    public decimal? DiscountLcu { get; set; }

    public decimal? ValueLcu { get; set; }

    public decimal? VatLcu { get; set; }

    public decimal? ValueVatLcu { get; set; }

    /// <summary>What the stock the line moved cost, from the cost layers it took (first in, first out), in the company's currency; set on posting. New.</summary>
    public decimal? CostLcu { get; set; }

    /// <summary>On the sale of a fixed asset: what it cost, in the company's currency.</summary>
    public decimal? AssetCostLcu { get; set; }

    /// <summary>On the sale of a fixed asset: its depreciation so far.</summary>
    public decimal? AssetDepreciatedLcu { get; set; }

    /// <summary>On the sale of a fixed asset: its book value when sold.</summary>
    public decimal? AssetBookValueLcu { get; set; }

    public bool Posted { get; set; }

    public Guid? ElementTypeId { get; set; }

    public decimal? Debit { get; set; }

    public decimal? Credit { get; set; }

    /// <summary>A journal line's own document number, typed by the user.</summary>
    public string? ReferenceNumber { get; set; }

    /// <summary>The lot, as typed or picked while the document is a draft.</summary>
    public string? Lot { get; set; }

    public DateOnly? LotExpiryDate { get; set; }

    /// <summary>The register row, from posting on.</summary>
    public Guid? ProductLotId { get; set; }

    /// <summary>The type of the document a journal line stands for.</summary>
    public Guid? ReferenceDocumentTypeId { get; set; }
}

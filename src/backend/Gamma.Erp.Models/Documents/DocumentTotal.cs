using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

[Table("tbl_document_total")]
public sealed class DocumentTotal : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid? DocumentId { get; set; }

    public Guid? CurrencyId { get; set; }

    public decimal? Discount { get; set; }

    public decimal? Value { get; set; }

    public decimal? Vat { get; set; }

    public decimal? ValueVat { get; set; }

    public decimal? ExchangeRate { get; set; }

    public decimal? DiscountLcu { get; set; }

    public decimal? ValueLcu { get; set; }

    public decimal? VatLcu { get; set; }

    public decimal? ValueVatLcu { get; set; }
}

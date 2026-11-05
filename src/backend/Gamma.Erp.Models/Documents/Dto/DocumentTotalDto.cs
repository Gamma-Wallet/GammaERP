using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Documents.Dto;

public sealed record DocumentTotalDto : RowDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    public Guid? DocumentId { get; init; }

    public Guid? CurrencyId { get; init; }

    public decimal? Discount { get; init; }

    public decimal? Value { get; init; }

    public decimal? Vat { get; init; }

    public decimal? ValueVat { get; init; }

    public decimal? ExchangeRate { get; init; }

    public decimal? DiscountLcu { get; init; }

    public decimal? ValueLcu { get; init; }

    public decimal? VatLcu { get; init; }

    public decimal? ValueVatLcu { get; init; }
}

namespace Gamma.Erp.Models.Partners.Dto;

/// <summary>A partner's figures from posted documents (CRM statistics).</summary>
public sealed record PartnerStatisticsDto(
    Guid PartnerId,
    string? Name,
    decimal Balance,
    IReadOnlyList<PartnerCurrencyBalanceDto> Currencies,
    DateOnly? FirstDocument,
    DateOnly? LastDocument,
    IReadOnlyList<PartnerCategoryFiguresDto> Categories,
    IReadOnlyList<PartnerMonthDto> Months,
    IReadOnlyList<PartnerProductDto> TopProducts);

/// <summary>The partner's balance in one currency: debit − credit, in that currency and in the local one.</summary>
public sealed record PartnerCurrencyBalanceDto(Guid? CurrencyId, string? Currency, decimal Debit, decimal Credit, decimal DebitLcu, decimal CreditLcu)
{
    public decimal Balance => Debit - Credit;
}

/// <summary>The partner's posted documents of one document category (as configured: sales, purchases, cash, bank…), in the local currency.</summary>
public sealed record PartnerCategoryFiguresDto(Guid CategoryId, string? Category, int Documents, decimal Value, decimal Vat, decimal ValueVat, decimal Debit, decimal Credit);

/// <summary>One month of one document category: the value with VAT of the partner's posted documents.</summary>
public sealed record PartnerMonthDto(string Month, Guid CategoryId, string? Category, decimal ValueVat);

/// <summary>A product bought from or sold to the partner: quantity and value.</summary>
public sealed record PartnerProductDto(Guid ProductId, string? Product, decimal Quantity, decimal Value);

/// <summary>A row of the partner's ledger: a posted document with its debit, credit and the running balance.</summary>
public sealed record PartnerLedgerRowDto(Guid? DocumentId, string? Code, DateOnly? PostingDate, string? Category, decimal Debit, decimal Credit, decimal Balance);

/// <summary>A partner in a ranking for one document category and period.</summary>
public sealed record PartnerRankDto(Guid PartnerId, string? Name, int Documents, decimal Debit, decimal Credit)
{
    public decimal Balance => Debit - Credit;
}

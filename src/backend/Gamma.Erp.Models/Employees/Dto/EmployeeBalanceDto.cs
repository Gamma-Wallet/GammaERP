using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Employees.Dto;

public sealed record EmployeeBalanceDto : RowDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    public Guid? EmployeeId { get; init; }

    public Guid? DocumentId { get; init; }

    public Guid? CurrencyId { get; init; }

    public Guid? ExchangeRateId { get; init; }

    public decimal? Debit { get; init; }

    public decimal? Credit { get; init; }

    public decimal? DebitLcu { get; init; }

    public decimal? CreditLcu { get; init; }

    public decimal? ExchangeRate { get; init; }
}

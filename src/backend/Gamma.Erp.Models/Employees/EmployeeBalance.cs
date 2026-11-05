using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Employees;

[Table("tbl_employee_balance")]
public sealed class EmployeeBalance : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid? EmployeeId { get; set; }

    public Guid? DocumentId { get; set; }

    public Guid? CurrencyId { get; set; }

    public Guid? ExchangeRateId { get; set; }

    public decimal? Debit { get; set; }

    public decimal? Credit { get; set; }

    public decimal? DebitLcu { get; set; }

    public decimal? CreditLcu { get; set; }

    public decimal? ExchangeRate { get; set; }
}

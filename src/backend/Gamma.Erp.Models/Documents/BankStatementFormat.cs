using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>How one bank writes its statement file: configuration. Columns count from 1; a statement has either one amount column, or one for money in and one for money out.</summary>
[Table("tbl_bank_statement_format")]
public sealed class BankStatementFormat : Row
{
    public TranslatedText Name { get; set; } = new();

    /// <summary>What separates the columns.</summary>
    public string Separator { get; set; } = ",";

    /// <summary>How many lines at the top of the file are not statement lines.</summary>
    public int HeaderLines { get; set; }

    public int DateColumn { get; set; }

    /// <summary>How the file writes a date, e.g. dd/MM/yyyy.</summary>
    public string DateFormat { get; set; } = "yyyy-MM-dd";

    public int TextColumn { get; set; }

    public int? ReferenceColumn { get; set; }

    /// <summary>Money in positive, money out negative.</summary>
    public int? AmountColumn { get; set; }

    public int? MoneyInColumn { get; set; }

    public int? MoneyOutColumn { get; set; }

    /// <summary>True when the file writes 1.234,56 instead of 1,234.56.</summary>
    public bool DecimalComma { get; set; }
}

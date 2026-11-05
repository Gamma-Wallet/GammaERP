using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

/// <summary>
/// A line of a financial statement, in its tree (id_sup): which accounts it holds is configuration, by the start of their codes, so a new account
/// falls in by itself.
/// </summary>
public abstract class StatementLine : Row
{
    public string? Code { get; set; }

    public TranslatedText? Name { get; set; }

    public double SortOrder { get; set; }

    /// <summary>The accounts it holds: the starts of their codes, by commas (31, 32, 35). The longest start that fits wins; empty on a heading.</summary>
    public string? Accounts { get; set; }

    /// <summary>Only accounts with this balance: 0 any, 1 debit, 2 credit (a client in credit is a liability).</summary>
    public int BalanceSide { get; set; }

    /// <summary>The line shows credit less debit (income, liabilities, equity); otherwise debit less credit.</summary>
    public bool CreditSide { get; set; }

    /// <summary>Calculated, not from accounts: 1 the result of the period, 2 the result of earlier years; 0 from its accounts.</summary>
    public int ResultKind { get; set; }
}

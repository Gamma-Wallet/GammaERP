using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// A provider's word that money arrived on one of the company's accounts: the bank's signed "payment received" message (and, later, the ERP's own Bank
/// of Cyprus payment or a card gateway's callback). A notice posts nothing: the till shows "Bank confirmed" and the cashier presses Paid, which keeps the
/// line on the bank's statement and fills <see cref="BankStatementLineId"/>. The same message twice keeps one notice.
/// </summary>
[Table("tbl_bank_notice")]
public sealed class BankNotice : Row
{
    /// <summary>bank (a bank's message or payment) or card (a card gateway's callback).</summary>
    public string Kind { get; set; } = "bank";

    /// <summary>The company's open banking provider whose secret signed the message.</summary>
    public Guid? ProviderId { get; set; }

    /// <summary>For a card notice: the company's card gateway (JCC) that took the payment.</summary>
    public Guid? CardProviderId { get; set; }

    /// <summary>The bank account (a cost centre) the money arrived on.</summary>
    public Guid? BankId { get; set; }

    /// <summary>The bank's own id of the line (its entry reference): unique per provider, and the id a later Fetch from bank recognises.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>The message's own id, as the provider sent it.</summary>
    public string? MessageId { get; set; }

    /// <summary>Money in, positive.</summary>
    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>The payer's reference text: the bill's code.</summary>
    public string? Reference { get; set; }

    /// <summary>The provider's reference of the payment (e.g. a Bank of Cyprus refNumber).</summary>
    public string? ProviderReference { get; set; }

    public string? PayerName { get; set; }

    public string? PayerIban { get; set; }

    /// <summary>True when the bank booked it; false while it is still pending.</summary>
    public bool Booked { get; set; }

    public DateOnly BookedOn { get; set; }

    public DateTimeOffset ReceivedOn { get; set; }

    /// <summary>The bank statement line the cashier's Paid made from it; null until then.</summary>
    public Guid? BankStatementLineId { get; set; }
}

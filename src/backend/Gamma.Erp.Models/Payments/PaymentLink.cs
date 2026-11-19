using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// A till bill's payment link (plan 4.5, the founder 2026-11-18): <c>&lt;erp&gt;/pay/{PayToken}</c>, shown as the till's QR code. Its page, open to anyone with
/// the link and showing no personal data, offers the company's ways to pay (Bank of Cyprus, card). A paid link has written a bank notice; when its payment
/// method says so (PaysOnConfirmation) the ERP then pays the bill itself, else the cashier presses Paid at the till.
/// </summary>
[Table("tbl_payment_link")]
public sealed class PaymentLink : Row
{
    /// <summary>The random part of the address: unguessable, one per bill and amount.</summary>
    public string PayToken { get; set; } = string.Empty;

    /// <summary>The till's point of sale, and the payment method it was made for.</summary>
    public Guid PointOfSaleId { get; set; }

    public Guid PaymentMethodId { get; set; }

    /// <summary>The bank account (cost centre) the money goes to.</summary>
    public Guid BankId { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>The bills' codes: the payment's reference.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Open, Paid, Expired or Cancelled.</summary>
    public string Status { get; set; } = "Open";

    /// <summary>How it was paid, e.g. BankOfCyprus.</summary>
    public string? Method { get; set; }

    /// <summary>The provider's id of the payment (Bank of Cyprus paymentId).</summary>
    public string? ProviderPaymentId { get; set; }

    /// <summary>The provider's reference of the done payment (Bank of Cyprus refNumber).</summary>
    public string? ProviderReference { get; set; }

    /// <summary>The bank notice the paid link wrote (or found).</summary>
    public Guid? BankNoticeId { get; set; }

    public DateTimeOffset ExpiresOn { get; set; }

    public DateTimeOffset? PaidOn { get; set; }

    /// <summary>What was given back to the card (JCC refunds), in the link's currency.</summary>
    public decimal RefundedAmount { get; set; }

    /// <summary>Started from Gamma Wallet: the return pages send the payer back into the app.</summary>
    public bool FromApp { get; set; }

    /// <summary>When the confirmation began paying the bill by itself (the method's PaysOnConfirmation); set once, so two confirmations never pay twice.</summary>
    public DateTimeOffset? SettledOn { get; set; }

    /// <summary>The payment document the confirmation posted.</summary>
    public Guid? PaymentId { get; set; }

    /// <summary>Why the confirmation could not pay the bill by itself; the cashier then presses Paid.</summary>
    public string? SettleError { get; set; }

    /// <summary>
    /// "Pay with Gamma app" (plan 10): the open bill as Gamma holds it, sent when the cashier chose the method (Bill/Open) — Gamma's bill id, its
    /// code and link (the QR the till's Retailer app shows). Paid, the same bill is marked paid (Bill/Paid) instead of a new one.
    /// </summary>
    public string? GammaBillId { get; set; }

    public string? GammaBillCode { get; set; }

    public string? GammaLink { get; set; }

    /// <summary>Why Gamma did not take the open bill (the till shows it; choosing the method again sends it again).</summary>
    public string? GammaErrors { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// A till payment's bill sent to Gamma Wallet (plan 4.5): one per payment (the receipt of money), sent only once the payment is confirmed and, for
/// bills the company declares, the tax authority confirmed. Gamma answers its own bill id, code, link and QR; the customer claims the reward by scanning
/// it, or through the payment link in Gamma Wallet. Nothing retries by itself: Send again and Check claim are a person's presses.
/// </summary>
[Table("tbl_wallet_bill")]
public sealed class WalletBill : Row
{
    /// <summary>The payment (receipt of money) whose bills it is.</summary>
    public Guid DocumentId { get; set; }

    public Guid WalletProviderId { get; set; }

    /// <summary>The bills' codes, as Gamma's reference (the same the payment link shows).</summary>
    public string Reference { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>Held (waiting for the tax authority), Waiting (sent, not claimed), Claimed, or Failed.</summary>
    public string Status { get; set; } = "Held";

    public string? GammaBillId { get; set; }

    /// <summary>The public code inside Gamma's QR.</summary>
    public string? Code { get; set; }

    /// <summary>Gamma's link the QR holds.</summary>
    public string? Link { get; set; }

    public DateTimeOffset? SentOn { get; set; }

    public DateTimeOffset? ClaimedOn { get; set; }

    public int Attempts { get; set; }

    public string? Errors { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

[Table("tbl_payment_method")]
public sealed class PaymentMethod : Row
{
    public string? Code { get; set; }

    public TranslatedText? Name { get; set; }

    public TranslatedText? Description { get; set; }

    /// <summary>What it takes goes in the drawer and is counted when a shift closes.</summary>
    public bool IsCash { get; set; }

    /// <summary>
    /// The till shows a payment QR code (EPC069-12, euro) for the bank its link at the point of sale names; the payer scans it with their banking app
    /// (the founder, 2026-11-14).
    /// </summary>
    public bool ShowsBankQr { get; set; }

    /// <summary>
    /// The payment provider the method uses (data, not code): empty for none; BankOfCyprus = the till shows the ERP's payment link, whose page pays
    /// through the company's Bank of Cyprus provider.
    /// </summary>
    public string? ProviderKind { get; set; }

    /// <summary>Its till payments are sent to Gamma Wallet as bills (plan 4.5; packs: every till method but store credits).</summary>
    public bool PushesToWallet { get; set; }

    /// <summary>
    /// The bank's or card gateway's confirmation of a payment link pays the bill (the founder, 2026-11-19): the ERP posts the payment as the cashier who made
    /// the link, at its till, then declares it and sends it to Gamma Wallet. Off: the cashier presses Paid on "Bank confirmed" / "Card paid".
    /// </summary>
    public bool PaysOnConfirmation { get; set; }
}

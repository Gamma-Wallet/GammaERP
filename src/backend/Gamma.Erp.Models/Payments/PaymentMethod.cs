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
}

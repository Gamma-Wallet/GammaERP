using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// One declared bill (plan 4.3c): declared only after the payment is confirmed (a bank or card notice matched by the cashier's Paid). Confirmed holds the
/// authority's answer (MARK / FIC / CSV / ATCUD) and the fiscal QR text; Refused or Declared (no answer) show Send again at the till. Nothing retries by itself.
/// </summary>
[Table("tbl_fiscal_receipt")]
public sealed class FiscalReceipt : Row
{
    /// <summary>The bill.</summary>
    public Guid DocumentId { get; set; }

    public Guid FiscalProviderId { get; set; }

    public Guid FiscalSeriesId { get; set; }

    /// <summary>The number in the series, given when the authority confirms.</summary>
    public int? Number { get; set; }

    /// <summary>The record's hash or the country's own key of it (IIC, huella, UID).</summary>
    public string? Hash { get; set; }

    /// <summary>Declared (sent, no answer yet), Confirmed or Refused.</summary>
    public string Status { get; set; } = "Declared";

    /// <summary>The authority's code for it (MARK, FIC, CSV, ATCUD).</summary>
    public string? AuthorityCode { get; set; }

    /// <summary>The text of the fiscal QR code printed on the bill.</summary>
    public string? QrText { get; set; }

    /// <summary>The authority's whole answer, as given.</summary>
    public JsonNode? Answer { get; set; }

    /// <summary>The bank statement line the Paid press matched (the payment's confirmation).</summary>
    public Guid? BankStatementLineId { get; set; }

    /// <summary>How it was paid (its authority code comes from the provider's codes), and the bank's or card's proof of it.</summary>
    public Guid? PaymentMethodId { get; set; }

    public string? PaymentReference { get; set; }

    public string? Errors { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? DeclaredOn { get; set; }

    public DateTimeOffset? ConfirmedOn { get; set; }
}

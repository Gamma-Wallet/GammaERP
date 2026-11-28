using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// One invoice on the e-invoicing network (Jinius): a sales invoice the company sent (Sent) or an invoice a supplier sent it (Received). It keeps the network's
/// id and its last known state; the state changes only when a person presses Send, Check, Approve, Reject or Enter. Nothing runs by itself.
/// </summary>
[Table("tbl_einvoice_link")]
public sealed class EInvoiceLink : Row
{
    public Guid EInvoiceProviderId { get; set; }

    /// <summary>Sent (the company's sales invoice) or Received (a supplier's invoice to the company).</summary>
    public string Direction { get; set; } = "Sent";

    /// <summary>The ERP document: the sales invoice sent, or the purchase invoice a received one was entered as; empty until then.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>The network's id of the invoice.</summary>
    public string? ExternalId { get; set; }

    /// <summary>The invoice's number as the network knows it.</summary>
    public string? ExternalNumber { get; set; }

    /// <summary>The network's state of the invoice (e.g. Pending, Approved, Paid, Rejected, Void), or Refused when the network did not take it.</summary>
    public string Status { get; set; } = "New";

    /// <summary>The state of its latest payment on the network (Completed, PendingApproval, Rejected…).</summary>
    public string? PaymentStatus { get; set; }

    /// <summary>The invoice's total.</summary>
    public decimal? Amount { get; set; }

    /// <summary>The other company's name and VAT number as the network gave them.</summary>
    public string? PartnerName { get; set; }

    public string? PartnerVatNumber { get; set; }

    /// <summary>The ERP payment booked when the network showed the invoice paid (Check, a person's press).</summary>
    public Guid? PaymentId { get; set; }

    /// <summary>The network's last answer, as given.</summary>
    public JsonNode? Answer { get; set; }

    /// <summary>Why the last step failed.</summary>
    public string? Errors { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? SentOn { get; set; }

    public DateTimeOffset? CheckedOn { get; set; }

    public DateTimeOffset? PaidOn { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// A company's e-invoicing network connection (Jinius, the founder 2026-11-28): where its B2B invoices go as structured e-invoices and where it reads the
/// invoices sent to it. One per company. Its keys are sealed; everything the exchange needs to decide is data in <see cref="Codes"/>: the network's VAT and
/// unit codes for the company's VAT groups and units, which company and partner properties hold the tax numbers and address, the payment document that books a
/// paid invoice, and how a received invoice is entered.
/// </summary>
[Table("tbl_einvoice_provider")]
public sealed class EInvoiceProvider : Row
{
    /// <summary>The network: Jinius.</summary>
    public string Kind { get; set; } = "Jinius";

    public string? Name { get; set; }

    /// <summary>Integration (the network's test environment) or Production.</summary>
    public string Environment { get; set; } = "Integration";

    /// <summary>The network's API address, e.g. https://api.integration.platform.cy.</summary>
    public string ApiBase { get; set; } = string.Empty;

    /// <summary>The API client made in the network's settings (Developer).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The client's secret, sealed.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The network's codes and the company's choices, as data:
    /// <c>{ "vat": { "&lt;VAT group id&gt;": "NineteenPercent" }, "unit": { "&lt;unit id&gt;": "Pieces" }, "defaultUnit": "Pieces",
    /// "company": { "vat": "TAX_NUMBER", "tin": "TAX_NUMBER", "registration": "REG_NUMBER" }, "partner": { … the same keys … }, "dueDays": 30,
    /// "documentTypes": [ "&lt;document type id&gt;" ], "payment": { "documentTypeId": "…", "bankId": "&lt;bank cost centre id&gt;" },
    /// "received": { "documentTypeId": "…", "transactionTypeId": "…", "typeConfId": "…", "elementId": "…", "vat": { "NineteenPercent": "&lt;VAT group id&gt;" } } }</c>.
    /// </summary>
    public JsonNode? Codes { get; set; }
}

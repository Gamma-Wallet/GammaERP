using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// A company's tax authority connection (plan 4.3c, the founder 2026-11-16): which country adapter declares its bills and where. One per company. Its codes
/// (VAT categories, payment types) are data: <see cref="Codes"/> maps the company's VAT groups and payment methods to the authority's own codes.
/// </summary>
[Table("tbl_fiscal_provider")]
public sealed class FiscalProvider : Row
{
    /// <summary>The adapter: GreeceMyData, CyprusMyData, AlbaniaFiscalisation, SpainVeriFactu or PortugalAtcud.</summary>
    public string Kind { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>Simulator (the tax authority simulator), Test or Production.</summary>
    public string Environment { get; set; } = "Simulator";

    /// <summary>The authority's API address (the simulator's, here).</summary>
    public string ApiBase { get; set; } = string.Empty;

    /// <summary>The seller's tax number as the authority knows it (ΑΦΜ, TIC, NIPT, NIF).</summary>
    public string IssuerTaxNumber { get; set; } = string.Empty;

    /// <summary>The seller's signing certificate or key (Albania: IIC; Portugal: the software's key), sealed; empty when the country needs none.</summary>
    public string? Certificate { get; set; }

    /// <summary>Portugal: the certified software's number.</summary>
    public string? CertificateNumber { get; set; }

    /// <summary>Albania: the software's code; Spain: the invoicing system's id.</summary>
    public string? SoftwareCode { get; set; }

    /// <summary>
    /// The authority's codes, by the company's rows: <c>{ "vat": { "&lt;vat group id&gt;": "1" }, "payment": { "&lt;payment method id&gt;": "7" } }</c>.
    /// </summary>
    public JsonNode? Codes { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// A company's card payment gateway (plan 4.5, the founder 2026-11-18): the shop's own JCC merchant account (card payments need no payment licence, only
/// JCC's merchant contract). One per company. The password and the callback key are sealed by the host and never returned.
/// </summary>
[Table("tbl_card_provider")]
public sealed class CardProvider : Row
{
    /// <summary>Which adapter talks to it: Jcc.</summary>
    public string Kind { get; set; } = "Jcc";

    public string? Name { get; set; }

    /// <summary>Test (JCC's test gateway) or Production.</summary>
    public string Environment { get; set; } = "Test";

    /// <summary>The gateway's address up to /payment, e.g. https://gateway-test.jcc.com.cy/payment.</summary>
    public string ApiBase { get; set; } = string.Empty;

    /// <summary>The merchant's API user (&lt;login&gt;-api).</summary>
    public string ApiUser { get; set; } = string.Empty;

    /// <summary>The API user's password, sealed.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>The key JCC signs its callbacks with (checksum), sealed; empty when callbacks are not checked (they are then refused).</summary>
    public string? CallbackKey { get; set; }

    /// <summary>Where JCC sends the payer back after the card page: the ERP's /pay/card-return; its origin is also the payment links' address.</summary>
    public string ReturnUrl { get; set; } = string.Empty;
}

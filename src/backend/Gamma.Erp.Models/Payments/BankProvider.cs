using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// A company's open banking provider: the app the company registered with the provider and its key (the founder, 2026-11-14: each company keeps its own
/// open banking data, entered when the company is made). The provider reads the company's bank accounts under its own licence. Never copied to another
/// company, and the key is never shown again.
/// </summary>
[Table("tbl_bank_provider")]
public sealed class BankProvider : Row
{
    /// <summary>Which adapter talks to it, e.g. EnableBanking.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>What people read.</summary>
    public string? Name { get; set; }

    /// <summary>Sandbox (test banks) or Production (real accounts).</summary>
    public string Environment { get; set; } = "Sandbox";

    /// <summary>The provider's API address.</summary>
    public string ApiBase { get; set; } = string.Empty;

    /// <summary>The company's application at the provider.</summary>
    public string ApplicationId { get; set; } = string.Empty;

    /// <summary>The application's private key, sealed by the host; never returned.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>A short fingerprint of the key's public half, to show which key is set.</summary>
    public string? KeyFingerprint { get; set; }

    /// <summary>Where the bank sends the person back after they sign in; one of the application's allowed addresses.</summary>
    public string RedirectUrl { get; set; } = string.Empty;

    /// <summary>How many days a bank connection asks for, 1 to 180.</summary>
    public int AccessDays { get; set; } = 180;
}

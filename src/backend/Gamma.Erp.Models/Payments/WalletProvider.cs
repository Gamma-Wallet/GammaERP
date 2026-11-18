using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// The company's Gamma Wallet connection (plan 4.5, the founder 2026-11-18): the retailer's access to Gamma, kept by the ERP. The token is the
/// business's own integration token (GWINT_…, made in Gamma Business › Integrations), sealed by the host and never returned; the business's name,
/// id and currency are what Gamma answered (Connection/Me) when it was saved. One per company; the ERP pushes its paid bills, not rewards.
/// </summary>
[Table("tbl_wallet_provider")]
public sealed class WalletProvider : Row
{
    /// <summary>GammaWallet.</summary>
    public string Kind { get; set; } = "GammaWallet";

    /// <summary>The Integration API's address, e.g. https://integration.gamma-wallet.com.</summary>
    public string ApiBase { get; set; } = string.Empty;

    /// <summary>The business's integration token, sealed.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>The token's last characters, to show which one is set.</summary>
    public string? TokenFingerprint { get; set; }

    /// <summary>The Gamma business (retailer) the token belongs to.</summary>
    public string? GammaBusinessId { get; set; }

    public string? BusinessName { get; set; }

    /// <summary>The business's currency at Gamma; it must be the company's own.</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>Whether customers can claim rewards now (the business has an active reward service).</summary>
    public bool CanClaim { get; set; }

    public DateTimeOffset? CheckedOn { get; set; }
}

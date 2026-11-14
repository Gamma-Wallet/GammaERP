using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// A bank account cost centre's identity and, when connected, its link to the account at the bank through the company's open banking provider. Without a
/// provider it holds only the IBAN, BIC and holder (enough for a payment QR code). One per bank cost centre.
/// </summary>
[Table("tbl_bank_connection")]
public sealed class BankConnection : Row
{
    /// <summary>The bank account: a cost centre whose type takes bank statements.</summary>
    public Guid BankId { get; set; }

    public string? Iban { get; set; }

    public string? Bic { get; set; }

    /// <summary>The account holder's name, as the bank knows it (shown to a payer).</summary>
    public string? HolderName { get; set; }

    /// <summary>The account's currency code, from the bank.</summary>
    public string? Currency { get; set; }

    /// <summary>The company's provider the account is connected through; empty when it is not connected.</summary>
    public Guid? ProviderId { get; set; }

    /// <summary>The bank as the provider names it.</summary>
    public string? AspspName { get; set; }

    public string? AspspCountry { get; set; }

    /// <summary>personal or business.</summary>
    public string? PsuType { get; set; }

    /// <summary>The provider's session, sealed by the host.</summary>
    public string? SessionId { get; set; }

    /// <summary>The provider's id of the account.</summary>
    public string? AccountUid { get; set; }

    /// <summary>When the bank's access ends; the account is connected again before then.</summary>
    public DateTimeOffset? ValidUntil { get; set; }

    /// <summary>The last day fetched from the bank: the next fetch starts there.</summary>
    public DateOnly? LastFetchedTo { get; set; }

    /// <summary>A connection started and not finished yet: the state the bank sends back.</summary>
    public string? PendingState { get; set; }

    /// <summary>The accounts the bank gave when there was more than one to choose from (uid, IBAN, currency, name), until one is chosen.</summary>
    public JsonNode? PendingAccounts { get; set; }

    /// <summary>The user who connected it.</summary>
    public Guid? ConnectedBy { get; set; }

    public DateTimeOffset? ConnectedOn { get; set; }
}

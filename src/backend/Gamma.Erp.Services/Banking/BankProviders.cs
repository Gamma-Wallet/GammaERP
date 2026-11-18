using System.Security.Cryptography;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Banking;

/// <summary>An adapter the ERP has for a provider: its kind, its name and its API address by default.</summary>
public sealed record BankProviderKind(string Kind, string Name, string ApiBase);

/// <summary>
/// A company's provider as entered: the kind, the application and its private key (PEM), where the bank sends the person back, and optionally the
/// environment, how long a connection asks for, a name and the API address. On a change an empty key keeps the key already set.
/// </summary>
public sealed record BankProviderInput(string Kind, string ApplicationId, string? PrivateKey, string? RedirectUrl, string? Environment = null, int? AccessDays = null,
    string? Name = null, string? ApiBase = null, string? ResponseSecret = null, bool? PaymentsEnabled = null, string? PaymentReturnUrl = null, int? PaymentLinkMinutes = null);

/// <summary>A company's provider as shown: never the key, only whether one is set, its fingerprint, and whether the host can still open it.</summary>
public sealed record BankProviderDto(Guid Id, string Kind, string? Name, string Environment, string ApiBase, string ApplicationId, bool KeySet, string? KeyFingerprint,
    bool KeyReadable, string RedirectUrl, int AccessDays, bool ResponseSecretSet = false, bool PaymentsEnabled = false, string? PaymentReturnUrl = null, int PaymentLinkMinutes = 20);

/// <summary>
/// The company's open banking provider (the founder, 2026-11-14): one per company, entered when the company is made or later by an administrator under
/// Configuration › Open banking. The private key is sealed by the host and never returned. Not part of a pack and never copied to another company.
/// </summary>
public sealed class BankProviders(ErpDbContext db, RequestContext request, Visibility visibility, ISecretBox secrets)
{
    public const int MaxAccessDays = 180;

    /// <summary>The adapters the ERP has.</summary>
    public static IReadOnlyList<BankProviderKind> Kinds { get; } =
    [
        new("EnableBanking", "Enable Banking", "https://api.enablebanking.com"),
        // Bank of Cyprus's own PSD2 API (the founder, 2026-11-18): the application id is the app's client id and the key is its client secret.
        new(BankOfCyprusConnector.KindName, "Bank of Cyprus", "https://sandbox-apis.bankofcyprus.com/df-boc-org-sb/sb/psd2"),
    ];

    /// <summary>Test banks, or real accounts.</summary>
    public static IReadOnlyList<string> Environments { get; } = ["Sandbox", "Production"];

    /// <summary>The company's provider, or null when it has none.</summary>
    public async Task<BankProviderDto?> GetAsync(CancellationToken ct)
    {
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<BankProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false);
        return row is null ? null : ToDto(row, secrets);
    }

    /// <summary>Sets the company's provider: a new one, or a change of the one it has (the old row kept as history).</summary>
    public async Task<BankProviderDto> SaveAsync(BankProviderInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<BankProvider>().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false);
        if (row is null)
        {
            row = Make(input, secrets, request.TenantId, company);
            db.Add(row);
        }
        else
        {
            Fill(row, input, secrets, keyRequired: false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(row, secrets);
    }

    /// <summary>Switches the company's provider off: its bank connections can no longer read the bank until one is set again.</summary>
    public async Task SwitchOffAsync(CancellationToken ct)
    {
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<BankProvider>().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("provider", "The company has no open banking provider.");
        row.Active = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A new provider row for a company, checked; used when a company is made too. Saving is the caller's.</summary>
    public static BankProvider Make(BankProviderInput input, ISecretBox secrets, Guid tenantId, Guid company)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = new BankProvider { TenantId = tenantId, OwnerId = company };
        Fill(row, input, secrets, keyRequired: true);
        return row;
    }

    public static BankProviderDto ToDto(BankProvider row, ISecretBox secrets)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(secrets);
        var set = row.PrivateKey.Length > 0;
        return new BankProviderDto(row.Id, row.Kind, row.Name, row.Environment, row.ApiBase, row.ApplicationId, set, row.KeyFingerprint, set && secrets.Open(row.PrivateKey) is not null,
            row.RedirectUrl, row.AccessDays, row.ResponseSecret is { Length: > 0 }, row.PaymentsEnabled, row.PaymentReturnUrl, row.PaymentLinkMinutes);
    }

    /// <summary>The fingerprint of a key's public half: the first 8 bytes of its SHA-256, in hex pairs.</summary>
    public static string Fingerprint(RSA key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var hash = SHA256.HashData(key.ExportSubjectPublicKeyInfo());
        return string.Join(':', hash.Take(8).Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>Checks every field and writes them on the row; refuses with every reason at once.</summary>
    private static void Fill(BankProvider row, BankProviderInput input, ISecretBox secrets, bool keyRequired)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        var errors = new Dictionary<string, string>();
        var kind = Kinds.FirstOrDefault(k => string.Equals(k.Kind, input.Kind?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (kind is null)
        {
            errors["kind"] = $"The ERP has no adapter for {input.Kind}; it has {string.Join(", ", Kinds.Select(k => k.Kind))}.";
        }

        var environment = Environments.FirstOrDefault(e => string.Equals(e, (input.Environment ?? row.Environment ?? "Sandbox").Trim(), StringComparison.OrdinalIgnoreCase));
        if (environment is null)
        {
            errors["environment"] = "The environment is Sandbox or Production.";
        }

        var application = input.ApplicationId?.Trim() ?? string.Empty;
        if (application.Length is 0 or > 100 || application.Any(char.IsWhiteSpace))
        {
            errors["applicationId"] = "The application id is the provider's id of the company's application (no spaces).";
        }

        var redirect = string.IsNullOrWhiteSpace(input.RedirectUrl) ? row.RedirectUrl : input.RedirectUrl.Trim();
        if (!Address(redirect, httpAllowed: true))
        {
            errors["redirectUrl"] = "Where the bank sends the person back: a full http or https address, one of the application's allowed addresses.";
        }

        var apiBase = string.IsNullOrWhiteSpace(input.ApiBase) ? (row.ApiBase is { Length: > 0 } held && kind?.Kind == row.Kind ? held : kind?.ApiBase ?? string.Empty) : input.ApiBase.Trim().TrimEnd('/');
        if (kind is not null && !Address(apiBase, httpAllowed: false))
        {
            errors["apiBase"] = "The provider's API address is a full https address.";
        }

        var days = input.AccessDays ?? row.AccessDays;
        if (days is < 1 or > MaxAccessDays)
        {
            errors["accessDays"] = $"A bank connection asks for 1 to {MaxAccessDays} days.";
        }

        string? sealedKey = null, fingerprint = null;
        if (kind?.Kind == BankOfCyprusConnector.KindName && !string.IsNullOrWhiteSpace(input.PrivateKey))
        {
            // Bank of Cyprus: the app's client secret, a text, not a key file.
            var secret = input.PrivateKey.Trim();
            if (secret.Length is < 8 or > 200 || secret.Any(char.IsWhiteSpace))
            {
                errors["privateKey"] = "The client secret is the Bank of Cyprus app's secret: 8 to 200 characters, no spaces.";
            }
            else
            {
                (sealedKey, fingerprint) = (secrets.Seal(secret), string.Join(':', SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)).Take(8)
                    .Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))));
            }
        }
        else if (!string.IsNullOrWhiteSpace(input.PrivateKey))
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(input.PrivateKey);
                if (rsa.KeySize < 2048)
                {
                    errors["privateKey"] = "The private key is shorter than 2048 bits.";
                }
                else
                {
                    // Signing proves it is a private key, not only a public one.
                    rsa.SignData([1], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    (sealedKey, fingerprint) = (secrets.Seal(input.PrivateKey.Trim()), Fingerprint(rsa));
                }
            }
            catch (Exception e) when (e is ArgumentException or CryptographicException)
            {
                errors["privateKey"] = "The private key is not an RSA private key in PEM form (the .pem file the provider gave).";
            }
        }
        else if (keyRequired || row.PrivateKey.Length == 0 || row.Kind != kind?.Kind)
        {
            // A new provider, or another kind (its key would not fit), needs its own key.
            errors["privateKey"] = kind?.Kind == BankOfCyprusConnector.KindName ? "Give the Bank of Cyprus app's client secret." : "Give the application's private key (the .pem file).";
        }

        // The secret the bank signs its "payment received" messages with: the same text is set at the bank. Empty keeps the one set.
        string? sealedResponse = null;
        if (!string.IsNullOrWhiteSpace(input.ResponseSecret))
        {
            var response = input.ResponseSecret.Trim();
            if (response.Length is < 16 or > 200)
            {
                errors["responseSecret"] = "The bank's response secret is 16 to 200 characters, the same as set at the bank.";
            }
            else
            {
                sealedResponse = secrets.Seal(response);
            }
        }

        // Payments from the ERP's payment link page: Bank of Cyprus only, with the address the bank sends the payer back to (the ERP's /pay/bank-return).
        var payments = input.PaymentsEnabled ?? row.PaymentsEnabled;
        var paymentReturn = string.IsNullOrWhiteSpace(input.PaymentReturnUrl) ? row.PaymentReturnUrl : input.PaymentReturnUrl.Trim();
        var minutes = input.PaymentLinkMinutes ?? row.PaymentLinkMinutes;
        if (payments && kind?.Kind != BankOfCyprusConnector.KindName)
        {
            errors["paymentsEnabled"] = "Payments from the payment link are made through Bank of Cyprus only.";
        }
        else if (payments && !Address(paymentReturn, httpAllowed: true))
        {
            errors["paymentReturnUrl"] = "Where the bank sends the payer back: the ERP's address and /pay/bank-return, one of the app's redirect URIs at the bank.";
        }

        if (minutes is < 1 or > 1440)
        {
            errors["paymentLinkMinutes"] = "A payment link lasts 1 to 1440 minutes.";
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        (row.PaymentsEnabled, row.PaymentReturnUrl, row.PaymentLinkMinutes) = (payments, paymentReturn, minutes);
        if (sealedResponse is not null)
        {
            row.ResponseSecret = sealedResponse;
        }

        row.Kind = kind!.Kind;
        row.Name = string.IsNullOrWhiteSpace(input.Name) ? row.Name ?? kind.Name : input.Name.Trim();
        (row.Environment, row.ApiBase, row.ApplicationId, row.RedirectUrl, row.AccessDays) = (environment!, apiBase, application, redirect, days);
        if (sealedKey is not null)
        {
            (row.PrivateKey, row.KeyFingerprint) = (sealedKey, fingerprint);
        }
    }

    private static bool Address(string? value, bool httpAllowed) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || (httpAllowed && uri.Scheme == Uri.UriSchemeHttp)) && string.IsNullOrEmpty(uri.Fragment);

    private void Administrator()
    {
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: only an administrator sets the company's open banking.");
        }
    }

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "Open banking belongs to a company: choose one of its nodes.");
}

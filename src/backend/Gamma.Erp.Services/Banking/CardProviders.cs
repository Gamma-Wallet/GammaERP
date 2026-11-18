using Gamma.Erp.Models.Payments;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Banking;

/// <summary>A company's card gateway as entered: an empty password or callback key keeps the one set.</summary>
public sealed record CardProviderInput(string ApiUser, string? Password, string? CallbackKey, string? ReturnUrl, string? ApiBase = null, string? Environment = null,
    string? Name = null, string Kind = JccConnector.KindName);

/// <summary>As shown: never the password or the key, only whether they are set and readable.</summary>
public sealed record CardProviderDto(Guid Id, string Kind, string? Name, string Environment, string ApiBase, string ApiUser, bool PasswordSet, bool PasswordReadable,
    bool CallbackKeySet, string ReturnUrl);

/// <summary>
/// The company's card gateway (the founder, 2026-11-18): the shop's own JCC merchant account, one per company, set by an administrator under Configuration ›
/// Open banking (Card payments). Not part of a pack; never copied to another company.
/// </summary>
public sealed class CardProviders(ErpDbContext db, RequestContext request, Visibility visibility, ISecretBox secrets)
{
    /// <summary>JCC's test gateway, the default address.</summary>
    public const string TestGateway = "https://gateway-test.jcc.com.cy/payment";

    public async Task<CardProviderDto?> GetAsync(CancellationToken ct)
    {
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<CardProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false);
        return row is null ? null : ToDto(row);
    }

    public async Task<CardProviderDto> SaveAsync(CardProviderInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<CardProvider>().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false);
        var isNew = row is null;
        row ??= new CardProvider { TenantId = request.TenantId, OwnerId = company };
        var errors = new Dictionary<string, string>();
        if (!string.Equals(input.Kind, JccConnector.KindName, StringComparison.OrdinalIgnoreCase))
        {
            errors["kind"] = "The ERP has an adapter for JCC only.";
        }

        var user = input.ApiUser?.Trim() ?? string.Empty;
        if (user.Length is 0 or > 100 || user.Any(char.IsWhiteSpace))
        {
            errors["apiUser"] = "The merchant's API user (<login>-api), no spaces.";
        }

        var apiBase = string.IsNullOrWhiteSpace(input.ApiBase) ? (row.ApiBase.Length > 0 ? row.ApiBase : TestGateway) : input.ApiBase.Trim().TrimEnd('/');
        if (!Address(apiBase, httpAllowed: false))
        {
            errors["apiBase"] = "The gateway's address is a full https address up to /payment.";
        }

        var back = string.IsNullOrWhiteSpace(input.ReturnUrl) ? row.ReturnUrl : input.ReturnUrl.Trim();
        if (!Address(back, httpAllowed: true))
        {
            errors["returnUrl"] = "Where JCC sends the payer back: the ERP's address and /pay/card-return.";
        }

        var environment = (input.Environment ?? row.Environment).Trim() is "Production" ? "Production" : "Test";
        if (string.IsNullOrWhiteSpace(input.Password) && (isNew || row.Password.Length == 0))
        {
            errors["password"] = "Give the API user's password.";
        }

        if (input.CallbackKey is { Length: > 0 } key && key.Trim().Length < 8)
        {
            errors["callbackKey"] = "The callback key JCC gave (8 characters or more).";
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        (row.Kind, row.ApiUser, row.ApiBase, row.ReturnUrl, row.Environment) = (JccConnector.KindName, user, apiBase, back, environment);
        row.Name = string.IsNullOrWhiteSpace(input.Name) ? row.Name ?? "JCC" : input.Name.Trim();
        if (!string.IsNullOrWhiteSpace(input.Password))
        {
            row.Password = secrets.Seal(input.Password.Trim());
        }

        if (!string.IsNullOrWhiteSpace(input.CallbackKey))
        {
            row.CallbackKey = secrets.Seal(input.CallbackKey.Trim());
        }

        if (isNew)
        {
            db.Add(row);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(row);
    }

    public async Task SwitchOffAsync(CancellationToken ct)
    {
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<CardProvider>().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("provider", "The company has no card gateway.");
        row.Active = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private CardProviderDto ToDto(CardProvider row) => new(row.Id, row.Kind, row.Name, row.Environment, row.ApiBase, row.ApiUser, row.Password.Length > 0,
        row.Password.Length > 0 && secrets.Open(row.Password) is not null, row.CallbackKey is { Length: > 0 }, row.ReturnUrl);

    private static bool Address(string? value, bool httpAllowed) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || (httpAllowed && uri.Scheme == Uri.UriSchemeHttp)) && string.IsNullOrEmpty(uri.Fragment);

    private void Administrator()
    {
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: only an administrator sets the company's card payments.");
        }
    }

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "Card payments belong to a company: choose one of its nodes.");
}

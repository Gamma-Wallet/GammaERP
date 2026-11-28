using System.Text.Json.Nodes;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Banking;

/// <summary>The e-invoicing connection as entered; an empty secret keeps the one set.</summary>
public sealed record EInvoiceProviderInput(string ApiBase, string ClientId, JsonNode? Codes, string? ClientSecret = null, string? Environment = null, string? Name = null,
    string? Kind = null);

/// <summary>As shown: never the secret, only whether one is set.</summary>
public sealed record EInvoiceProviderDto(Guid Id, string Kind, string? Name, string Environment, string ApiBase, string ClientId, bool SecretSet, JsonNode? Codes);

/// <summary>The connection test: the token, and whether the network knows the company by the tax numbers its properties hold.</summary>
public sealed record EInvoiceTestDto(bool TokenOk, bool? CompanyFound, string? VatNumber, string? TaxIdNumber, string? RegistrationNumber, string? Problem);

/// <summary>Whether the network knows a partner: its tax numbers as the partner's properties hold them, and the network's answer.</summary>
public sealed record EInvoicePartnerDto(Guid PartnerId, string? Name, string? VatNumber, string? TaxIdNumber, string? RegistrationNumber, bool? Found, string? NetworkName,
    string? Problem);

/// <summary>The three tax numbers the network identifies a company by.</summary>
public sealed record EInvoiceIds(string? Vat, string? Tin, string? Registration)
{
    public int Count => new[] { Vat, Tin, Registration }.Count(v => !string.IsNullOrWhiteSpace(v));
}

/// <summary>
/// The company's e-invoicing connection (Jinius, the founder 2026-11-28): one per company, set by an administrator. Which properties hold the tax numbers, the
/// network's VAT and unit codes and the documents used are the row's codes, never code.
/// </summary>
public sealed class EInvoiceProviders(ErpDbContext db, RequestContext request, Visibility visibility, ISecretBox secrets, JiniusClient jinius)
{
    public const string Jinius = "Jinius";

    public async Task<EInvoiceProviderDto?> GetAsync(CancellationToken ct)
    {
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<EInvoiceProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false);
        return row is null ? null : ToDto(row);
    }

    public async Task<EInvoiceProviderDto> SaveAsync(EInvoiceProviderInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<EInvoiceProvider>().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false);
        var isNew = row is null;
        row ??= new EInvoiceProvider { TenantId = request.TenantId, OwnerId = company };
        var errors = new Dictionary<string, string>();
        if (input.Kind is { Length: > 0 } kind && !string.Equals(kind.Trim(), Jinius, StringComparison.OrdinalIgnoreCase))
        {
            errors["kind"] = $"The ERP connects to {Jinius}.";
        }

        if (!Uri.TryCreate(input.ApiBase?.Trim(), UriKind.Absolute, out var api) || api.Scheme != Uri.UriSchemeHttps)
        {
            errors["apiBase"] = "The network's API address is a full https address.";
        }

        if (string.IsNullOrWhiteSpace(input.ClientId))
        {
            errors["clientId"] = "The API client's id (the network's Settings › Developer).";
        }

        if (isNew && string.IsNullOrWhiteSpace(input.ClientSecret))
        {
            errors["clientSecret"] = "The API client's secret.";
        }

        if (input.Codes is not null and not JsonObject)
        {
            errors["codes"] = "The codes are an object (vat, unit, company, partner, payment, received…).";
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        (row.Kind, row.ApiBase, row.ClientId) = (Jinius, input.ApiBase!.Trim().TrimEnd('/'), input.ClientId.Trim());
        (row.Name, row.Environment) = (input.Name?.Trim() ?? row.Name ?? Jinius, input.Environment?.Trim() is { Length: > 0 } env ? env : row.Environment);
        row.Codes = input.Codes?.DeepClone() ?? row.Codes;
        if (!string.IsNullOrWhiteSpace(input.ClientSecret))
        {
            row.ClientSecret = secrets.Seal(input.ClientSecret.Trim());
        }

        if (isNew)
        {
            db.Add(row);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(row);
    }

    /// <summary>Test (a person's press): the token, then whether the network knows the company by its own tax numbers.</summary>
    public async Task<EInvoiceTestDto> TestAsync(CancellationToken ct)
    {
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var provider = await db.Set<EInvoiceProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("provider", $"{ErrorCodes.NotConfiguredError}: the company has no e-invoicing connection.");
        var secret = Secret(provider);
        try
        {
            await jinius.TokenAsync(provider, secret, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            return new EInvoiceTestDto(false, null, null, null, null, e.Message);
        }

        var ids = await CompanyIdsAsync(db, provider, company, ct).ConfigureAwait(false);
        var (found, _, problem) = await FindAsync(provider, secret, ids, ct).ConfigureAwait(false);
        return new EInvoiceTestDto(true, found, ids.Vat, ids.Tin, ids.Registration, problem);
    }

    /// <summary>Whether the network knows this partner (before sending it an invoice).</summary>
    public async Task<EInvoicePartnerDto> PartnerAsync(Guid partnerId, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var provider = await db.Set<EInvoiceProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("provider", $"{ErrorCodes.NotConfiguredError}: the company has no e-invoicing connection.");
        var partner = await db.Set<Partner>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == partnerId && p.Active, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("partnerId", "There is no such partner.");
        var ids = await PartnerIdsAsync(db, provider, partnerId, ct).ConfigureAwait(false);
        var (found, name, problem) = await FindAsync(provider, Secret(provider), ids, ct).ConfigureAwait(false);
        return new EInvoicePartnerDto(partnerId, partner.Name.In(Models.Base.TranslatedText.DefaultLanguage), ids.Vat, ids.Tin, ids.Registration, found, name, problem);
    }

    /// <summary>The company's tax numbers, read from the company properties the codes name (codes.company: vat, tin, registration).</summary>
    public static async Task<EInvoiceIds> CompanyIdsAsync(ErpDbContext db, EInvoiceProvider provider, Guid company, CancellationToken ct)
    {
        var map = provider.Codes?["company"] as JsonObject;
        var codes = Codes(map);
        var values = await (from v in db.Set<CostCenterInfo>().AsNoTracking() join t in db.Set<CostCenterTypeInfo>().AsNoTracking() on v.TypeInfoId equals t.Id
                            where v.ElementId == company && v.Active && t.Active && t.Code != null && codes.Contains(t.Code) select new { t.Code, v.Value })
            .ToListAsync(ct).ConfigureAwait(false);
        string? Of(string key) => (string?)map?[key] is { } code ? Text(values.FirstOrDefault(v => v.Code == code)?.Value) : null;
        return new EInvoiceIds(Of("vat"), Of("tin"), Of("registration"));
    }

    /// <summary>A partner's tax numbers, read from the partner properties the codes name (codes.partner: vat, tin, registration).</summary>
    public static async Task<EInvoiceIds> PartnerIdsAsync(ErpDbContext db, EInvoiceProvider provider, Guid partner, CancellationToken ct)
    {
        var map = provider.Codes?["partner"] as JsonObject;
        var codes = Codes(map);
        var values = await (from v in db.Set<PartnerInfo>().AsNoTracking() join t in db.Set<PartnerTypeInfo>().AsNoTracking() on v.TypeInfoId equals t.Id
                            where v.ElementId == partner && v.Active && t.Active && t.Code != null && codes.Contains(t.Code) select new { t.Code, v.Value })
            .ToListAsync(ct).ConfigureAwait(false);
        string? Of(string key) => (string?)map?[key] is { } code ? Text(values.FirstOrDefault(v => v.Code == code)?.Value) : null;
        return new EInvoiceIds(Of("vat"), Of("tin"), Of("registration"));
    }

    /// <summary>A property's value as plain text (a translated text gives its default or first language), without spaces.</summary>
    public static string? Text(JsonNode? value)
    {
        var text = value switch
        {
            JsonObject texts => (texts[Models.Base.TranslatedText.DefaultLanguage] ?? texts.FirstOrDefault().Value)?.ToString(),
            JsonValue plain => plain.ToString(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Replace(" ", "", StringComparison.Ordinal).Trim();
    }

    private static List<string> Codes(JsonObject? map) =>
        map is null ? [] : [.. map.Select(p => (string?)p.Value).OfType<string>().Distinct(StringComparer.Ordinal)];

    /// <summary>The network's legal-entity search with the numbers there are (it needs two of the three).</summary>
    private async Task<(bool? Found, string? Name, string? Problem)> FindAsync(EInvoiceProvider provider, string secret, EInvoiceIds ids, CancellationToken ct)
    {
        if (ids.Count < 2)
        {
            return (null, null, $"{ErrorCodes.NotConfiguredError}: the network needs two of VAT number, tax id and registration number; the properties hold {ids.Count}.");
        }

        var query = new List<string>();
        if (ids.Vat is { } vat)
        {
            query.Add($"VatNumber={Uri.EscapeDataString(vat)}");
        }

        if (ids.Tin is { } tin)
        {
            query.Add($"TaxIdentificationNumber={Uri.EscapeDataString(tin)}");
        }

        if (ids.Registration is { } reg)
        {
            query.Add($"CompanyRegistrationNumber={Uri.EscapeDataString(reg)}");
        }

        try
        {
            var answer = await jinius.CallAsync(provider, secret, HttpMethod.Get, $"/v3/legalEntities?{string.Join('&', query)}", null, ct).ConfigureAwait(false);
            if (!answer.Ok)
            {
                return (null, null, answer.Problem);
            }

            var first = (answer.Body?["legalEntities"] as JsonArray)?.FirstOrDefault();
            return (first is not null, (string?)first?["name"], null);
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            return (null, null, e.Message);
        }
    }

    private string Secret(EInvoiceProvider provider) =>
        secrets.Open(provider.ClientSecret) ?? throw ValidationException.For("clientSecret", "The client's secret can't be read here: enter it again.");

    private static EInvoiceProviderDto ToDto(EInvoiceProvider r) =>
        new(r.Id, r.Kind, r.Name, r.Environment, r.ApiBase, r.ClientId, r.ClientSecret is { Length: > 0 }, r.Codes);

    private void Administrator()
    {
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: only an administrator sets the company's e-invoicing connection.");
        }
    }

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "E-invoicing belongs to a company: choose one of its nodes.");
}

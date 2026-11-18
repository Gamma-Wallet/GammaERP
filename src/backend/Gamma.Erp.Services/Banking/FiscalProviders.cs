using System.Text.Json.Nodes;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Banking;

/// <summary>A company's tax authority connection as entered; an empty certificate keeps the one set.</summary>
public sealed record FiscalProviderInput(string Kind, string ApiBase, string IssuerTaxNumber, JsonNode? Codes, string? Certificate = null, string? CertificateNumber = null,
    string? SoftwareCode = null, string? Environment = null, string? Name = null);

/// <summary>As shown: never the certificate, only whether one is set.</summary>
public sealed record FiscalProviderDto(Guid Id, string Kind, string? Name, string Environment, string ApiBase, string IssuerTaxNumber, bool CertificateSet,
    string? CertificateNumber, string? SoftwareCode, JsonNode? Codes);

/// <summary>
/// The company's tax authority connection (plan 4.3c): one per company, set by an administrator. The adapters the ERP has are code; which one a company uses,
/// its address, its tax number and every authority code are this row.
/// </summary>
public sealed class FiscalProviders(ErpDbContext db, RequestContext request, Visibility visibility, ISecretBox secrets, IEnumerable<IFiscalAdapter> adapters)
{
    public async Task<FiscalProviderDto?> GetAsync(CancellationToken ct)
    {
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<FiscalProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false);
        return row is null ? null : ToDto(row);
    }

    public IReadOnlyList<string> Kinds() => [.. adapters.Select(a => a.Kind).Order(StringComparer.Ordinal)];

    public async Task<FiscalProviderDto> SaveAsync(FiscalProviderInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<FiscalProvider>().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false);
        var isNew = row is null;
        row ??= new FiscalProvider { TenantId = request.TenantId, OwnerId = company };
        var errors = new Dictionary<string, string>();
        var kind = adapters.FirstOrDefault(a => string.Equals(a.Kind, input.Kind?.Trim(), StringComparison.OrdinalIgnoreCase))?.Kind;
        if (kind is null)
        {
            errors["kind"] = $"The ERP has adapters for {string.Join(", ", Kinds())}.";
        }

        if (!Uri.TryCreate(input.ApiBase?.Trim(), UriKind.Absolute, out var api) || api.Scheme != Uri.UriSchemeHttps)
        {
            errors["apiBase"] = "The tax authority's API address is a full https address.";
        }

        var tax = input.IssuerTaxNumber?.Trim() ?? string.Empty;
        if (tax.Length is 0 or > 30)
        {
            errors["issuerTaxNumber"] = "The company's tax number as the authority knows it.";
        }

        if (input.Codes is not null and not JsonObject)
        {
            errors["codes"] = "The codes are an object: { \"vat\": { \"<VAT group id>\": \"code\" }, \"payment\": { \"<payment method id>\": \"code\" } }.";
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        (row.Kind, row.ApiBase, row.IssuerTaxNumber) = (kind!, input.ApiBase!.Trim().TrimEnd('/'), tax);
        (row.Name, row.Environment) = (input.Name?.Trim() ?? row.Name ?? kind, input.Environment?.Trim() is { Length: > 0 } env ? env : row.Environment);
        (row.CertificateNumber, row.SoftwareCode) = (input.CertificateNumber?.Trim() ?? row.CertificateNumber, input.SoftwareCode?.Trim() ?? row.SoftwareCode);
        row.Codes = input.Codes?.DeepClone() ?? row.Codes;
        if (!string.IsNullOrWhiteSpace(input.Certificate))
        {
            row.Certificate = secrets.Seal(input.Certificate.Trim());
        }

        if (isNew)
        {
            db.Add(row);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(row);
    }

    private static FiscalProviderDto ToDto(FiscalProvider r) =>
        new(r.Id, r.Kind, r.Name, r.Environment, r.ApiBase, r.IssuerTaxNumber, r.Certificate is { Length: > 0 }, r.CertificateNumber, r.SoftwareCode, r.Codes);

    private void Administrator()
    {
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: only an administrator sets the company's tax authority.");
        }
    }

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "The tax authority belongs to a company: choose one of its nodes.");
}

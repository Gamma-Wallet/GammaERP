using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Banking;

/// <summary>What Gamma's Integration API answered about the token's business (Connection/Me).</summary>
public sealed record GammaBusiness(string? BusinessId, string? BusinessName, string? CurrencyCode, bool CanClaim);

/// <summary>A bill as Gamma holds it (Bill/Create, Bill/Get).</summary>
public sealed record GammaBill(string? BillId, string? Code, string? Link, bool Claimed, DateTimeOffset? ClaimedOn);

/// <summary>
/// Gamma Wallet's Integration API (the shops' plugins use the same): <c>Authorization: Bearer GWINT_…</c>; Connection/Me names the business,
/// Bill/Create declares a paid bill (the same reference and total again returns the same bill), Bill/Get tells whether it was claimed.
/// </summary>
public sealed class GammaIntegrationClient(HttpClient http)
{
    public async Task<GammaBusiness> MeAsync(string apiBase, string token, CancellationToken ct)
    {
        var json = await SendAsync(HttpMethod.Get, apiBase, "/api/Connection/Me", token, null, ct).ConfigureAwait(false);
        return new GammaBusiness((string?)json["businessId"], (string?)json["businessName"], (string?)json["currencyCode"], (bool?)json["canClaim"] ?? false);
    }

    public async Task<GammaBill> CreateBillAsync(string apiBase, string token, string reference, decimal total, string currency, DateTimeOffset issuedOn, string version,
        CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["reference"] = reference, ["total"] = total, ["currencyCode"] = currency, ["issuedOn"] = issuedOn.UtcDateTime, ["platform"] = "gamma-erp", ["pluginVersion"] = version,
        };
        return Bill(await SendAsync(HttpMethod.Post, apiBase, "/api/Bill/Create", token, body, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// "Pay with Gamma app" (plan 10): the bill while it is still to pay — Gamma shows it on the point of sale's Retailer app; the customer pays it from
    /// their own bank in Gamma Wallet, to the shop's account.
    /// </summary>
    public async Task<GammaBill> OpenBillAsync(string apiBase, string token, GammaOpenBill bill, string version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bill);
        var body = new JsonObject
        {
            ["reference"] = bill.Reference, ["total"] = bill.Total, ["currencyCode"] = bill.Currency, ["issuedOn"] = bill.IssuedOn.UtcDateTime,
            ["payee"] = new JsonObject { ["iban"] = bill.PayeeIban, ["name"] = bill.PayeeName, ["bic"] = bill.PayeeBic },
            ["pointOfSale"] = new JsonObject { ["code"] = bill.PointOfSaleCode, ["name"] = bill.PointOfSaleName },
            ["staffLogin"] = bill.StaffLogin, ["validUntil"] = bill.ValidUntil.UtcDateTime, ["platform"] = "gamma-erp", ["pluginVersion"] = version,
        };
        return Bill(await SendAsync(HttpMethod.Post, apiBase, "/api/Bill/Open", token, body, ct).ConfigureAwait(false));
    }

    /// <summary>The open bill was paid to the shop's account: the bank's payment text (Gamma finds its payment code in it) and reference.</summary>
    public async Task<GammaBill> MarkBillPaidAsync(string apiBase, string token, string billId, string? paymentText, string? bankReference, DateTimeOffset paidOn,
        CancellationToken ct)
    {
        var body = new JsonObject { ["billId"] = billId, ["paymentReference"] = paymentText, ["bankReference"] = bankReference, ["paidOn"] = paidOn.UtcDateTime };
        return Bill(await SendAsync(HttpMethod.Post, apiBase, "/api/Bill/Paid", token, body, ct).ConfigureAwait(false));
    }

    public async Task<GammaBill> GetBillAsync(string apiBase, string token, string billId, CancellationToken ct) =>
        Bill(await SendAsync(HttpMethod.Get, apiBase, "/api/Bill/Get/" + Uri.EscapeDataString(billId), token, null, ct).ConfigureAwait(false));

    private static GammaBill Bill(JsonNode json)
    {
        var status = json["status"]?.ToString();
        var claimed = string.Equals(status, "Claimed", StringComparison.OrdinalIgnoreCase) || status == "1";
        var claimedOn = DateTimeOffset.TryParse(json["claimedOn"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var on) ? on : (DateTimeOffset?)null;
        return new GammaBill((string?)json["billId"], (string?)json["code"], (string?)json["link"], claimed, claimedOn);
    }

    /// <summary>The call's answer (inside Gamma's envelope when it has one); a refusal becomes a <see cref="BankProviderException"/> with Gamma's reason.</summary>
    private async Task<JsonNode> SendAsync(HttpMethod method, string apiBase, string path, string token, JsonObject? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, apiBase.TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new BankProviderException("Gamma Wallet can't be reached: " + e.Message);
        }

        using (response)
        {
            JsonNode? json = null;
            try
            {
                json = await response.Content.ReadFromJsonAsync<JsonNode>(ct).ConfigureAwait(false);
            }
            catch (System.Text.Json.JsonException)
            {
                // Not JSON: the status says enough.
            }

            var result = json?["result"] ?? json;
            if (!response.IsSuccessStatusCode || result is null || (json?["error"] is { } error && error.GetValueKind() != System.Text.Json.JsonValueKind.Null))
            {
                var reason = (string?)json?["error"]?["message"] ?? (string?)json?["message"] ?? (string?)json?["title"] ?? response.ReasonPhrase ?? "refused";
                throw new BankProviderException($"Gamma Wallet refused ({(int)response.StatusCode}): {reason}");
            }

            return result;
        }
    }
}

/// <summary>An open bill for Gamma (plan 10): what the customer pays, to whom, at which point of sale and for which staff member's Retailer app.</summary>
public sealed record GammaOpenBill(string Reference, decimal Total, string Currency, DateTimeOffset IssuedOn, string PayeeIban, string PayeeName, string? PayeeBic,
    string? PointOfSaleCode, string? PointOfSaleName, string StaffLogin, DateTimeOffset ValidUntil);

/// <summary>The company's Gamma Wallet connection as entered: an empty token keeps the one set.</summary>
public sealed record WalletProviderInput(string? Token, string? ApiBase = null);

/// <summary>As shown: never the token; the business Gamma named for it.</summary>
public sealed record WalletProviderDto(Guid Id, string ApiBase, bool TokenSet, string? TokenFingerprint, string? GammaBusinessId, string? BusinessName, string? CurrencyCode,
    bool CanClaim, DateTimeOffset? CheckedOn);

/// <summary>
/// The company's Gamma Wallet connection (plan 4.5): the retailer's access, set by an administrator. Saving asks Gamma whose token it is (Connection/Me)
/// and refuses a business whose currency is not the company's.
/// </summary>
public sealed class WalletProviders(ErpDbContext db, RequestContext request, Visibility visibility, ISecretBox secrets, GammaIntegrationClient gamma, TimeProvider? clock = null)
{
    public const string DefaultApiBase = "https://integration.gamma-wallet.com";

    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public async Task<WalletProviderDto?> GetAsync(CancellationToken ct)
    {
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<WalletProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false);
        return row is null ? null : ToDto(row);
    }

    public async Task<WalletProviderDto> SaveAsync(WalletProviderInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        Administrator();
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<WalletProvider>().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false);
        var isNew = row is null;
        row ??= new WalletProvider { TenantId = request.TenantId, OwnerId = company };
        var apiBase = string.IsNullOrWhiteSpace(input.ApiBase) ? (row.ApiBase.Length > 0 ? row.ApiBase : DefaultApiBase) : input.ApiBase.Trim().TrimEnd('/');
        if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var api) || api.Scheme != Uri.UriSchemeHttps)
        {
            throw ValidationException.For("apiBase", "Gamma's Integration API address is a full https address.");
        }

        var token = input.Token?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            token = row.Token.Length > 0 ? secrets.Open(row.Token) : null;
        }
        else if (!token.StartsWith("GWINT_", StringComparison.Ordinal) || token.Length > 200 || token.Any(char.IsWhiteSpace))
        {
            throw ValidationException.For("token", "The integration token starts with GWINT_ (Gamma Business › Integrations).");
        }

        if (token is null)
        {
            throw ValidationException.For("token", "Give the business's integration token (GWINT_…).");
        }

        var business = await BankConnections.Call(() => gamma.MeAsync(apiBase, token, ct)).ConfigureAwait(false);
        var currency = await db.Set<Gamma.Erp.Models.Currencies.Currency>().AsNoTracking().Where(c => c.Active && c.IsBase && c.OwnerId == company).Select(c => c.Code)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (currency is not null && business.CurrencyCode is { Length: > 0 } theirs && !string.Equals(currency, theirs, StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationException.For("token", $"The Gamma business {business.BusinessName} works in {theirs}; the company in {currency}.");
        }

        (row.ApiBase, row.Token, row.TokenFingerprint) = (apiBase, secrets.Seal(token), "…" + token[^4..]);
        (row.GammaBusinessId, row.BusinessName, row.CurrencyCode, row.CanClaim, row.CheckedOn) = (business.BusinessId, business.BusinessName, business.CurrencyCode, business.CanClaim,
            time.GetUtcNow());
        if (isNew)
        {
            db.Add(row);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(row);
    }

    private static WalletProviderDto ToDto(WalletProvider r) =>
        new(r.Id, r.ApiBase, r.Token.Length > 0, r.TokenFingerprint, r.GammaBusinessId, r.BusinessName, r.CurrencyCode, r.CanClaim, r.CheckedOn);

    private void Administrator()
    {
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: only an administrator sets the company's Gamma Wallet connection.");
        }
    }

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "Gamma Wallet belongs to a company: choose one of its nodes.");
}

/// <summary>A payment's Gamma bill as the till shows it: its state, Gamma's QR (SVG of its link) and why it is held or failed.</summary>
public sealed record WalletBillDto(Guid Id, Guid DocumentId, string Reference, decimal Total, string Status, string? Link, string? QrSvg, string? Errors, int Attempts);

/// <summary>
/// Sending till payments to Gamma Wallet (plan 4.5, the gate of 2026-11-16): after the cashier's Paid on a confirmed payment, a method marked
/// PushesToWallet sends one Gamma bill per payment — but only when every bill it pays that the company declares is confirmed by the tax authority;
/// otherwise the bill is Held until Send again (of the fiscal receipt) confirms it. Gamma links the bill to a customer: by the QR, or through the
/// payment link in Gamma Wallet. The ERP never stores who the customer is.
/// </summary>
public sealed class GammaWalletBills(ErpDbContext db, Visibility visibility, ISecretBox secrets, GammaIntegrationClient gamma, TimeProvider? clock = null)
{
    public const string Version = "1.0.0";

    private readonly TimeProvider time = clock ?? TimeProvider.System;

    /// <summary>Called in the Paid press: the payment's Gamma bill, sent now when the gate allows, held otherwise; nothing when the method or company does not push.</summary>
    /// <remarks>A bill already open at Gamma ("Pay with Gamma app"): <paramref name="open"/> holds its id and the bank's payment text and reference; it is
    /// then marked paid instead of created.</remarks>
    public async Task<WalletBillDto?> AfterPaidAsync(Guid paymentId, IReadOnlyList<Guid> billIds, Guid paymentMethodId, string reference, decimal total, CancellationToken ct,
        OpenGammaBill? open = null)
    {
        var payment = await db.Set<Document>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == paymentId, ct).ConfigureAwait(false);
        var method = await db.Set<PaymentMethod>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == paymentMethodId, ct).ConfigureAwait(false);
        if (payment is null || method is not { PushesToWallet: true })
        {
            return null;
        }

        var provider = await db.Set<WalletProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == payment.CompanyId, ct).ConfigureAwait(false);
        if (provider is null)
        {
            return null;
        }

        var row = await db.Set<WalletBill>().FirstOrDefaultAsync(b => b.Active && b.DocumentId == paymentId, ct).ConfigureAwait(false);
        if (row is null)
        {
            var currency = await db.Set<Gamma.Erp.Models.Currencies.Currency>().AsNoTracking().Where(c => c.Active && c.IsBase && c.OwnerId == payment.CompanyId).Select(c => c.Code)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? provider.CurrencyCode ?? "EUR";
            row = new WalletBill
            {
                TenantId = payment.TenantId, OwnerId = payment.CompanyId, DocumentId = paymentId, WalletProviderId = provider.Id, Reference = reference.Trim(),
                Total = decimal.Round(total, 2, MidpointRounding.AwayFromZero), CurrencyCode = currency,
                GammaBillId = open?.GammaBillId, Code = open?.Code, Link = open?.Link, PaymentText = open?.PaymentText, BankReference = open?.BankReference,
            };
            db.Add(row);
        }

        await SendIfAllowedAsync(row, provider, billIds, ct).ConfigureAwait(false);
        return Dto(row);
    }

    /// <summary>Send again (a person's press): a held or failed bill is sent when the gate allows now.</summary>
    public async Task<WalletBillDto> SendAgainAsync(Guid id, CancellationToken ct)
    {
        var row = await MineAsync(id, ct).ConfigureAwait(false);
        if (row.Status is "Waiting" or "Claimed")
        {
            return Dto(row);
        }

        var provider = await ProviderAsync(row, ct).ConfigureAwait(false);
        await SendIfAllowedAsync(row, provider, await BillsOfAsync(row.DocumentId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        return Dto(row);
    }

    /// <summary>After a fiscal receipt is confirmed on Send again: the held Gamma bills of the company are tried again.</summary>
    public async Task ReleaseHeldAsync(Guid company, CancellationToken ct)
    {
        foreach (var row in await db.Set<WalletBill>().Where(b => b.Active && b.OwnerId == company && b.Status == "Held").ToListAsync(ct).ConfigureAwait(false))
        {
            var provider = await ProviderAsync(row, ct).ConfigureAwait(false);
            await SendIfAllowedAsync(row, provider, await BillsOfAsync(row.DocumentId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Check claim (a person's press): asks Gamma whether the customer claimed the reward.</summary>
    public async Task<WalletBillDto> CheckClaimAsync(Guid id, CancellationToken ct)
    {
        var row = await MineAsync(id, ct).ConfigureAwait(false);
        if (row.GammaBillId is null)
        {
            return Dto(row);
        }

        var provider = await ProviderAsync(row, ct).ConfigureAwait(false);
        var token = secrets.Open(provider.Token) ?? throw ValidationException.For("token", "The Gamma Wallet token can no longer be opened: an administrator enters it again.");
        var bill = await BankConnections.Call(() => gamma.GetBillAsync(provider.ApiBase, token, row.GammaBillId, ct)).ConfigureAwait(false);
        if (bill.Claimed)
        {
            (row.Status, row.ClaimedOn) = ("Claimed", bill.ClaimedOn ?? time.GetUtcNow());
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Dto(row);
    }

    /// <summary>The payments' Gamma bills, for the till.</summary>
    public async Task<IReadOnlyList<WalletBillDto>> OfPaymentsAsync(IReadOnlyList<Guid> paymentIds, CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var rows = await db.Set<WalletBill>().AsNoTracking().Where(b => b.Active && paymentIds.Contains(b.DocumentId) && b.OwnerId != null && nodes.Contains(b.OwnerId.Value))
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(Dto)];
    }

    /// <summary>The gate: every bill of the payment whose type is declared and whose company has a fiscal provider must be confirmed; then Bill/Create.</summary>
    private async Task SendIfAllowedAsync(WalletBill row, WalletProvider provider, IReadOnlyList<Guid> billIds, CancellationToken ct)
    {
        var declares = await db.Set<FiscalProvider>().AsNoTracking().AnyAsync(p => p.Active && p.OwnerId == row.OwnerId, ct).ConfigureAwait(false);
        if (declares)
        {
            var fiscalised = await db.Set<Document>().AsNoTracking().Where(d => billIds.Contains(d.Id))
                .Join(db.Set<DocumentType>().AsNoTracking().Where(t => t.Fiscalise), d => d.TypeId, t => t.Id, (d, _) => d.Id).ToListAsync(ct).ConfigureAwait(false);
            var confirmed = await db.Set<FiscalReceipt>().AsNoTracking().Where(r => r.Active && fiscalised.Contains(r.DocumentId) && r.Status == "Confirmed").Select(r => r.DocumentId)
                .Distinct().CountAsync(ct).ConfigureAwait(false);
            if (confirmed < fiscalised.Count)
            {
                (row.Status, row.Errors) = ("Held", "Held until the tax authority confirms the bill (Send again on the fiscal receipt).");
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return;
            }
        }

        var token = secrets.Open(provider.Token);
        row.Attempts++;
        if (token is null)
        {
            (row.Status, row.Errors) = ("Failed", "The Gamma Wallet token can no longer be opened: an administrator enters it again.");
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            // Open at Gamma already (paid with the Gamma app): the same bill becomes paid; else a new paid bill.
            var bill = row.GammaBillId is { } open && row.SentOn is null
                ? await gamma.MarkBillPaidAsync(provider.ApiBase, token, open, row.PaymentText, row.BankReference, time.GetUtcNow(), ct).ConfigureAwait(false)
                : await gamma.CreateBillAsync(provider.ApiBase, token, row.Reference, row.Total, row.CurrencyCode, time.GetUtcNow(), Version, ct).ConfigureAwait(false);
            (row.GammaBillId, row.Code, row.Link, row.SentOn, row.Errors) = (bill.BillId ?? row.GammaBillId, bill.Code ?? row.Code, bill.Link ?? row.Link, time.GetUtcNow(), null);
            (row.Status, row.ClaimedOn) = bill.Claimed ? ("Claimed", bill.ClaimedOn) : ("Waiting", null);
        }
        catch (BankProviderException e)
        {
            (row.Status, row.Errors) = ("Failed", e.Message);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The bills a payment pays (the documents it was made from).</summary>
    private async Task<IReadOnlyList<Guid>> BillsOfAsync(Guid paymentId, CancellationToken ct) =>
        await db.Set<InterDocumentDocument>().AsNoTracking().Where(l => l.Active && l.Document2Id == paymentId && l.Document1Id != null).Select(l => l.Document1Id!.Value)
            .ToListAsync(ct).ConfigureAwait(false);

    private async Task<WalletBill> MineAsync(Guid id, CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var row = await db.Set<WalletBill>().FirstOrDefaultAsync(b => b.Active && b.Id == id, ct).ConfigureAwait(false);
        return row is not null && row.OwnerId is { } owner && nodes.Contains(owner) ? row : throw ValidationException.For("id", "There is no such Gamma bill.");
    }

    private async Task<WalletProvider> ProviderAsync(WalletBill row, CancellationToken ct) =>
        await db.Set<WalletProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.Id == row.WalletProviderId, ct).ConfigureAwait(false)
        ?? throw ValidationException.For("provider", "The company's Gamma Wallet connection is switched off.");

    private static WalletBillDto Dto(WalletBill r) =>
        new(r.Id, r.DocumentId, r.Reference, r.Total, r.Status, r.Link, r.Link is { Length: > 0 } link ? PosBankPayments.Svg(link) : null, r.Errors, r.Attempts);
}

/// <summary>A bill Gamma holds as open, paid now: its id, code and link, and what the bank said (its text holds Gamma's payment code).</summary>
public sealed record OpenGammaBill(string GammaBillId, string? Code, string? Link, string? PaymentText, string? BankReference);

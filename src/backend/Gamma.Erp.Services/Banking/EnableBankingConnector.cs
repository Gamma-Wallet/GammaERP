using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Payments;

namespace Gamma.Erp.Services.Banking;

/// <summary>
/// Enable Banking (enablebanking.com): account information under its licence. Each call is signed with a short token made from the company's application key
/// (RS256: kid = the application id, iss enablebanking.com, aud api.enablebanking.com). Tried against its sandbox on 2026-11-14.
/// </summary>
public sealed class EnableBankingConnector(HttpClient http) : IBankConnector
{
    private static readonly string[] BalanceOrder = ["CLBD", "ITBD", "XPCD", "ITAV", "CLAV", "OTHR"];

    public string Kind => "EnableBanking";

    public async Task<IReadOnlyList<BankAspsp>> BanksAsync(BankProvider provider, string privateKey, string country, CancellationToken ct)
    {
        var answer = await SendAsync(HttpMethod.Get, provider, privateKey, $"/aspsps?country={Uri.EscapeDataString(country)}", null, null, ct).ConfigureAwait(false);
        return [.. (answer?["aspsps"]?.AsArray() ?? []).OfType<JsonObject>().Select(a => new BankAspsp((string?)a["name"] ?? string.Empty, (string?)a["country"] ?? country,
            [.. (a["psu_types"]?.AsArray() ?? []).Select(p => (string?)p).OfType<string>()])).Where(a => a.Name.Length > 0).OrderBy(a => a.Name, StringComparer.CurrentCulture)];
    }

    public async Task<string> StartAsync(BankProvider provider, string privateKey, BankAspsp bank, string psuType, string state, DateTimeOffset validUntil, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(bank);
        var body = new JsonObject
        {
            ["access"] = new JsonObject { ["valid_until"] = validUntil.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) },
            ["aspsp"] = new JsonObject { ["name"] = bank.Name, ["country"] = bank.Country },
            ["state"] = state,
            ["redirect_url"] = provider.RedirectUrl,
            ["psu_type"] = psuType,
        };
        var answer = await SendAsync(HttpMethod.Post, provider, privateKey, "/auth", body, null, ct).ConfigureAwait(false);
        return (string?)answer?["url"] ?? throw new BankProviderException("The provider gave no address to sign in at the bank.");
    }

    public async Task<BankSessionInfo> SessionAsync(BankProvider provider, string privateKey, string code, CancellationToken ct)
    {
        var answer = await SendAsync(HttpMethod.Post, provider, privateKey, "/sessions", new JsonObject { ["code"] = code }, null, ct).ConfigureAwait(false)
            ?? throw new BankProviderException("The provider gave no session.");
        var until = DateTimeOffset.TryParse((string?)answer["access"]?["valid_until"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var u) ? u : (DateTimeOffset?)null;
        var accounts = (answer["accounts"]?.AsArray() ?? []).OfType<JsonObject>().Select(a => new BankAccountInfo((string?)a["uid"] ?? string.Empty,
            (string?)a["account_id"]?["iban"], (string?)a["account_servicer"]?["bic_fi"], (string?)a["currency"], ((string?)a["name"])?.Trim())).Where(a => a.Uid.Length > 0).ToList();
        return new BankSessionInfo((string?)answer["session_id"] ?? throw new BankProviderException("The provider gave no session."), until, accounts);
    }

    public async Task<BankBalanceInfo?> BalanceAsync(BankProvider provider, string privateKey, string accountUid, PsuPresence psu, CancellationToken ct)
    {
        var answer = await SendAsync(HttpMethod.Get, provider, privateKey, $"/accounts/{Uri.EscapeDataString(accountUid)}/balances", null, psu, ct).ConfigureAwait(false);
        var balances = (answer?["balances"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        static int Rank(JsonObject b)
        {
            var i = Array.IndexOf(BalanceOrder, (string?)b["balance_type"]);
            return i < 0 ? int.MaxValue : i;
        }

        var best = balances.OrderBy(Rank).FirstOrDefault();
        if (best is null || !decimal.TryParse((string?)best["balance_amount"]?["amount"], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        return new BankBalanceInfo(amount, (string?)best["balance_amount"]?["currency"], Day((string?)best["reference_date"]));
    }

    public async Task<BankTransactionsPage> TransactionsAsync(BankProvider provider, string privateKey, string accountUid, DateOnly fromDay, DateOnly toDay, string? continuation, PsuPresence psu,
        CancellationToken ct)
    {
        var query = $"/accounts/{Uri.EscapeDataString(accountUid)}/transactions?date_from={fromDay:yyyy-MM-dd}&date_to={toDay:yyyy-MM-dd}"
            + (continuation is null ? string.Empty : $"&continuation_key={Uri.EscapeDataString(continuation)}");
        var answer = await SendAsync(HttpMethod.Get, provider, privateKey, query, null, psu, ct).ConfigureAwait(false);
        var lines = new List<BankTransactionInfo>();
        foreach (var t in (answer?["transactions"]?.AsArray() ?? []).OfType<JsonObject>())
        {
            if ((Day((string?)t["booking_date"]) ?? Day((string?)t["value_date"]) ?? Day((string?)t["transaction_date"])) is not { } date
                || !decimal.TryParse((string?)t["transaction_amount"]?["amount"], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                continue;
            }

            // Money in positive, out negative; the other party is the payer for money in and the payee for money out.
            var incoming = string.Equals((string?)t["credit_debit_indicator"], "CRDT", StringComparison.OrdinalIgnoreCase);
            var other = incoming ? t["debtor"] : t["creditor"];
            var otherAccount = incoming ? t["debtor_account"] : t["creditor_account"];
            var texts = (t["remittance_information"]?.AsArray() ?? []).Select(r => ((string?)r)?.Trim()).Where(r => !string.IsNullOrEmpty(r)).Distinct().ToList();
            lines.Add(new BankTransactionInfo(date, incoming ? Math.Abs(amount) : -Math.Abs(amount), (string?)t["transaction_amount"]?["currency"], (string?)t["entry_reference"],
                texts.Count == 0 ? null : string.Join(" ", texts), (string?)t["reference_number"], ((string?)other?["name"])?.Trim(), (string?)otherAccount?["iban"],
                !string.Equals((string?)t["status"], "PDNG", StringComparison.OrdinalIgnoreCase)));
        }

        var key = (string?)answer?["continuation_key"];
        return new BankTransactionsPage(lines, string.IsNullOrEmpty(key) ? null : key);
    }

    public async Task EndAsync(BankProvider provider, string privateKey, string sessionId, CancellationToken ct)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, provider, privateKey, $"/sessions/{Uri.EscapeDataString(sessionId)}", null, null, ct).ConfigureAwait(false);
        }
        catch (BankProviderException)
        {
            // Already ended or expired at the provider: nothing to end.
        }
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, BankProvider provider, string privateKey, string path, JsonNode? body, PsuPresence? psu, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        using var message = new HttpRequestMessage(method, provider.ApiBase.TrimEnd('/') + path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(provider.ApplicationId, privateKey));
        if (psu?.IpAddress is { Length: > 0 } ip)
        {
            message.Headers.TryAddWithoutValidation("Psu-Ip-Address", ip);
        }

        if (psu?.UserAgent is { Length: > 0 } agent)
        {
            message.Headers.TryAddWithoutValidation("Psu-User-Agent", agent);
        }

        if (body is not null)
        {
            message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new BankProviderException($"The provider can't be reached: {e.Message}");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            JsonNode? json = null;
            try
            {
                json = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
            }
            catch (System.Text.Json.JsonException)
            {
                // Not JSON: the status says enough.
            }

            if (!response.IsSuccessStatusCode)
            {
                var reason = (string?)json?["message"] ?? (string?)json?["detail"] ?? response.ReasonPhrase ?? "refused";
                throw new BankProviderException($"The provider refused ({(int)response.StatusCode}): {reason}");
            }

            return json;
        }
    }

    /// <summary>A token valid for an hour, signed with the application's key.</summary>
    internal static string Token(string applicationId, string privateKey)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = new JsonObject { ["typ"] = "JWT", ["alg"] = "RS256", ["kid"] = applicationId };
        var payload = new JsonObject { ["iss"] = "enablebanking.com", ["aud"] = "api.enablebanking.com", ["iat"] = now, ["exp"] = now + 3600 };
        var signed = $"{Base64Url(Encoding.UTF8.GetBytes(header.ToJsonString()))}.{Base64Url(Encoding.UTF8.GetBytes(payload.ToJsonString()))}";
        return $"{signed}.{Base64Url(rsa.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static DateOnly? Day(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}

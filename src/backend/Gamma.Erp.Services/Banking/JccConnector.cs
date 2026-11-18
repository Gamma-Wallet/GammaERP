using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Payments;

namespace Gamma.Erp.Services.Banking;

/// <summary>A JCC order's state: orderStatus (0 registered, 1 held, 2 deposited, 3 reversed, 4 refunded, 5 3-D Secure, 6 declined), the bank's codes and the card.</summary>
public sealed record JccOrderStatus(int OrderStatus, int? ActionCode, string? ActionText, long Amount, string? ApprovalCode, string? MaskedPan, string? OrderNumber)
{
    /// <summary>Paid: held (1) or deposited (2).</summary>
    public bool Paid => OrderStatus is 1 or 2;
}

/// <summary>
/// JCC's payment gateway (<c>https://gateway-test.jcc.com.cy/payment/rest/…</c>): form-encoded POSTs with the merchant's API user and password, amounts in
/// minor units, currency as ISO number (978 = EUR); every answer is HTTP 200 with errorCode ("0" = success). Callbacks carry a checksum: HMAC-SHA256 with the
/// merchant's key over the parameters sorted by name as "name;value;", upper-case hex. Checked against the simulator's copy (2026-11-18).
/// </summary>
public sealed class JccConnector(HttpClient http)
{
    public const string KindName = "Jcc";

    /// <summary>Registers a one-phase order; the answer is JCC's order id and its card page.</summary>
    public async Task<(string OrderId, string FormUrl)> RegisterAsync(CardProvider provider, string password, long amountMinor, string currency, string orderNumber,
        string returnUrl, string? callbackUrl, string description, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["amount"] = amountMinor.ToString(CultureInfo.InvariantCulture), ["currency"] = currency, ["orderNumber"] = orderNumber, ["returnUrl"] = returnUrl,
            ["failUrl"] = returnUrl, ["description"] = description, ["language"] = "en",
        };
        if (callbackUrl is not null)
        {
            form["dynamicCallbackUrl"] = callbackUrl;
        }

        var answer = await CallAsync(provider, password, "register.do", form, ct).ConfigureAwait(false);
        return ((string?)answer["orderId"] ?? throw new BankProviderException("JCC gave no order id."), (string?)answer["formUrl"] ?? throw new BankProviderException("JCC gave no card page."));
    }

    public async Task<JccOrderStatus> StatusAsync(CardProvider provider, string password, string orderId, CancellationToken ct)
    {
        var a = await CallAsync(provider, password, "getOrderStatusExtended.do", new Dictionary<string, string> { ["orderId"] = orderId }, ct).ConfigureAwait(false);
        return new JccOrderStatus(Int(a["orderStatus"]) ?? -1, Int(a["actionCode"]), (string?)a["actionCodeDescription"], (long?)Int(a["amount"]) ?? 0,
            (string?)a["cardAuthInfo"]?["approvalCode"], (string?)a["cardAuthInfo"]?["maskedPan"], (string?)a["orderNumber"]);
    }

    /// <summary>Gives money back to the card; the same <paramref name="refundId"/> again refunds nothing twice.</summary>
    public Task RefundAsync(CardProvider provider, string password, string orderId, long amountMinor, string refundId, CancellationToken ct) =>
        CallAsync(provider, password, "refund.do", new Dictionary<string, string>
        {
            ["orderId"] = orderId, ["amount"] = amountMinor.ToString(CultureInfo.InvariantCulture), ["externalRefundId"] = refundId,
        }, ct);

    /// <summary>Whether a callback's checksum is the merchant's (constant-time compare).</summary>
    public static bool ChecksumMatches(IReadOnlyDictionary<string, string> parameters, string key, string? checksum)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrEmpty(checksum))
        {
            return false;
        }

        var text = new StringBuilder();
        foreach (var (name, value) in parameters.Where(p => p.Key is not ("checksum" or "sign_alias")).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            text.Append(name).Append(';').Append(value).Append(';');
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(text.ToString()));
        byte[] given;
        try
        {
            given = Convert.FromHexString(checksum);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    private async Task<JsonNode> CallAsync(CardProvider provider, string password, string call, Dictionary<string, string> form, CancellationToken ct)
    {
        form["userName"] = provider.ApiUser;
        form["password"] = password;
        using var request = new HttpRequestMessage(HttpMethod.Post, provider.ApiBase.TrimEnd('/') + "/rest/" + call) { Content = new FormUrlEncodedContent(form) };
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new BankProviderException($"JCC can't be reached: {e.Message}");
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
                // Not JSON: refused below.
            }

            var code = (string?)json?["errorCode"] ?? json?["ErrorCode"]?.ToString();
            if (!response.IsSuccessStatusCode || json is null || (code is not null && code != "0"))
            {
                throw new BankProviderException($"JCC refused ({code ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)}): {(string?)json?["errorMessage"] ?? response.ReasonPhrase}");
            }

            return json;
        }
    }

    private static int? Int(JsonNode? node) =>
        node is null ? null : int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}

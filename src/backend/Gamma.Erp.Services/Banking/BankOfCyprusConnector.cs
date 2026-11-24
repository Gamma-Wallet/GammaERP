using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Payments;

namespace Gamma.Erp.Services.Banking;

/// <summary>
/// Bank of Cyprus PSD2 API (developer portal sandbox: <c>https://sandbox-apis.bankofcyprus.com/df-boc-org-sb/sb/psd2</c>). The company's provider row holds the
/// app's client id (<see cref="BankProvider.ApplicationId"/>) and client secret (the sealed <see cref="BankProvider.PrivateKey"/>).
/// <list type="bullet">
/// <item>Every call carries an app token (OAuth2 client credentials, scope TPPOAuth2Security) and the headers <c>journeyId</c> (a GUID) and <c>timeStamp</c>.</item>
/// <item>Connecting: a subscription is made (accounts: details, balance, transaction history), the shop approves it on the bank's consent page
/// (<c>/oauth2/authorize?…&amp;subscriptionid=…</c>), the code it sends back becomes a customer token (UserOAuth2Security), and that token activates the
/// subscription with the accounts the shop chose. The subscription id is the connection's session; reads send it in the <c>subscriptionId</c> header.</item>
/// </list>
/// Checked against the simulator's copy of the sandbox (Development\OpenBankingSimulation, 2026-11-18).
/// </summary>
public sealed class BankOfCyprusConnector(HttpClient http) : IBankConnector
{
    public const string KindName = "BankOfCyprus";

    /// <summary>The bank's statement dates.</summary>
    private const string DateFormat = "dd/MM/yyyy";

    public string Kind => KindName;

    /// <summary>One bank: Bank of Cyprus itself, in Cyprus.</summary>
    public Task<IReadOnlyList<BankAspsp>> BanksAsync(BankProvider provider, string privateKey, string country, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BankAspsp>>(string.Equals(country, "CY", StringComparison.OrdinalIgnoreCase)
            ? [new BankAspsp("Bank of Cyprus", "CY", ["business", "personal"])]
            : []);

    /// <summary>Makes the subscription and gives the consent page's address; the subscription id is kept until the shop comes back.</summary>
    public async Task<BankStartInfo> StartAsync(BankProvider provider, string privateKey, BankAspsp bank, string psuType, string state, DateTimeOffset validUntil,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var token = await AppTokenAsync(provider, privateKey, ct).ConfigureAwait(false);
        var body = new JsonObject
        {
            ["accounts"] = new JsonObject { ["transactionHistory"] = true, ["balance"] = true, ["details"] = true, ["checkFundsAvailability"] = false },
            ["payments"] = new JsonObject { ["limit"] = 0, ["currency"] = "EUR", ["amount"] = 0 },
        };
        var answer = await SendAsync(HttpMethod.Post, provider, "/v1/subscriptions", token, null, body, ct).ConfigureAwait(false);
        var subscription = (string?)answer?["subscriptionId"] ?? throw new BankProviderException("The bank gave no subscription id.");
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code", ["redirect_uri"] = provider.RedirectUrl, ["scope"] = "UserOAuth2Security", ["client_id"] = provider.ApplicationId,
            ["subscriptionid"] = subscription, ["state"] = state,
        };
        return new BankStartInfo(AuthorizeUrl(provider, query), subscription);
    }

    /// <summary>The code becomes a customer token, which activates the subscription with the accounts the shop chose; then the accounts are read.</summary>
    public async Task<BankSessionInfo> SessionAsync(BankProvider provider, string privateKey, string code, string? pending, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var subscription = pending ?? throw new BankProviderException("The connection was not started with a Bank of Cyprus subscription: start it again.");
        var user = await TokenAsync(provider, privateKey, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = provider.RedirectUrl, ["scope"] = "UserOAuth2Security",
        }, ct).ConfigureAwait(false);
        var app = await AppTokenAsync(provider, privateKey, ct).ConfigureAwait(false);

        // The accounts the shop allowed on the consent page.
        var allowed = await SendAsync(HttpMethod.Get, provider, $"/v1/subscriptions/{Uri.EscapeDataString(subscription)}", app, null, null, ct).ConfigureAwait(false);
        var selected = new JsonArray([.. (allowed?["selectedAccounts"]?.AsArray() ?? []).OfType<JsonObject>()
            .Select(a => (JsonNode)new JsonObject { ["accountId"] = (string?)a["accountId"] })]);
        var activate = new JsonObject
        {
            ["selectedAccounts"] = selected,
            ["accounts"] = allowed?["accounts"]?.DeepClone() ?? new JsonObject { ["transactionHistory"] = true, ["balance"] = true, ["details"] = true, ["checkFundsAvailability"] = false },
            ["payments"] = allowed?["payments"]?.DeepClone() ?? new JsonObject { ["limit"] = 0, ["currency"] = "EUR", ["amount"] = 0 },
        };
        var active = await SendAsync(HttpMethod.Patch, provider, $"/v1/subscriptions/{Uri.EscapeDataString(subscription)}", user, null, activate, ct).ConfigureAwait(false);
        var until = Day((string?)active?["duration"]?["endDate"]) is { } end ? new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : (DateTimeOffset?)null;

        var accounts = await SendAsync(HttpMethod.Get, provider, "/v1/accounts", app, subscription, null, ct).ConfigureAwait(false);
        return new BankSessionInfo(subscription, until, [.. (accounts?.AsArray() ?? []).OfType<JsonObject>().Where(a => !string.IsNullOrEmpty((string?)a["accountId"]))
            .Select(a => new BankAccountInfo(AccountUid(subscription, (string)a["accountId"]!), (string?)a["IBAN"], (string?)a["bankId"], (string?)a["currency"],
                (string?)a["accountName"]))]);
    }

    /// <summary>The CURRENT balance (the booked one).</summary>
    public async Task<BankBalanceInfo?> BalanceAsync(BankProvider provider, string privateKey, string accountUid, PsuPresence psu, CancellationToken ct)
    {
        var (subscription, account) = Split(accountUid);
        var token = await AppTokenAsync(provider, privateKey, ct).ConfigureAwait(false);
        var answer = await SendAsync(HttpMethod.Get, provider, $"/v1/accounts/{Uri.EscapeDataString(account)}/balance", token, subscription, null, ct).ConfigureAwait(false);
        var row = (answer is JsonArray list ? list.FirstOrDefault() : answer) as JsonObject;
        var current = (row?["balances"]?.AsArray() ?? []).OfType<JsonObject>().FirstOrDefault(b => (string?)b["balanceType"] == "CURRENT");
        return current is null ? null
            : new BankBalanceInfo(current["amount"]!.GetValue<decimal>(), (string?)row?["currency"], DateOnly.FromDateTime(DateTime.UtcNow));
    }

    /// <summary>The statement of the days asked, in one page: money in positive (CREDIT), out negative (DEBIT); the bank's line id is the line's own id.</summary>
    public async Task<BankTransactionsPage> TransactionsAsync(BankProvider provider, string privateKey, string accountUid, DateOnly fromDay, DateOnly toDay, string? continuation,
        PsuPresence psu, CancellationToken ct)
    {
        var (subscription, account) = Split(accountUid);
        var token = await AppTokenAsync(provider, privateKey, ct).ConfigureAwait(false);
        var path = $"/v1/accounts/{Uri.EscapeDataString(account)}/statement?startDate={Uri.EscapeDataString(fromDay.ToString(DateFormat, CultureInfo.InvariantCulture))}"
            + $"&endDate={Uri.EscapeDataString(toDay.ToString(DateFormat, CultureInfo.InvariantCulture))}";
        var answer = await SendAsync(HttpMethod.Get, provider, path, token, subscription, null, ct).ConfigureAwait(false);
        var lines = new List<BankTransactionInfo>();
        foreach (var t in (answer?["transaction"]?.AsArray() ?? []).OfType<JsonObject>())
        {
            var amount = t["transactionAmount"]?["amount"]?.GetValue<decimal>() ?? 0m;
            var credit = (string?)t["dcInd"] == "CREDIT";
            var date = Day((string?)t["postingDate"]) ?? Day((string?)t["valueDate"]) ?? fromDay;
            var text = (string?)t["description"];
            lines.Add(new BankTransactionInfo(date, credit ? Math.Abs(amount) : -Math.Abs(amount), (string?)t["transactionAmount"]?["currency"], (string?)t["id"], text, text,
                null, null, Booked: true));
        }

        return new BankTransactionsPage(lines, null);
    }

    // ------------------------------------------------------------------ payments (initiation, from the ERP's payment link page)

    /// <summary>
    /// Starts a payment to the shop's account: the payment JSON is signed (JWS; in the sandbox by the bank's sign helper, as its guide says — real payments
    /// need the company's own QSeal certificate and a payment-initiation licence), then initiated. The answer is the bank's payment id.
    /// </summary>
    public async Task<string> InitiatePaymentAsync(BankProvider provider, string secret, string? creditorBic, string creditorAccountId, decimal amount, string currency,
        string details, string endToEndId, BankPayer payer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!string.Equals(provider.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new BankProviderException("Bank of Cyprus payments are set up for the sandbox only: real payments need the company's payment-initiation licence and QSeal certificate.");
        }

        ArgumentNullException.ThrowIfNull(payer);

        // No debtor: the payer chooses the account at the bank (Bank of Cyprus refuses an empty one). The creditor needs its bank's BIC.
        var payment = new JsonObject
        {
            ["creditor"] = new JsonObject { ["bankId"] = creditorBic ?? string.Empty, ["accountId"] = creditorAccountId },
            ["transactionAmount"] = new JsonObject { ["amount"] = amount, ["currency"] = currency },
            ["endToEndId"] = endToEndId,
            ["paymentDetails"] = details,
        };
        using var sign = new HttpRequestMessage(HttpMethod.Post, SignUrl(provider)) { Content = Json(payment) };
        sign.Headers.Add("journeyId", Guid.NewGuid().ToString());
        sign.Headers.Add("timeStamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        // The sandbox sign helper answers 500 without it (its guide's value).
        sign.Headers.Add("tppId", "singpaymentdata");
        var signed = await ReadAsync(sign, ct).ConfigureAwait(false) as JsonObject ?? throw new BankProviderException("The bank's sign helper gave nothing.");
        var token = await AppTokenAsync(provider, secret, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var customer = new Dictionary<string, string>
        {
            ["customerDevice"] = payer.Device, ["customerIP"] = payer.Ip, ["customerSessionId"] = endToEndId, ["loginTimeStamp"] = now,
        };
        var answer = await SendAsync(HttpMethod.Post, provider, "/v1/payments/initiate", token, null, signed, ct, customer).ConfigureAwait(false);
        return (string?)answer?["payment"]?["paymentId"] ?? throw new BankProviderException("The bank gave no payment id.");
    }

    /// <summary>The bank's page where the payer approves the payment; it sends them back to <paramref name="returnUrl"/> with a code and the state.</summary>
    public static string ApprovalUrl(BankProvider provider, string paymentId, string returnUrl, string state)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code", ["redirect_uri"] = returnUrl, ["scope"] = "UserOAuth2Security", ["client_id"] = provider.ApplicationId, ["paymentid"] = paymentId,
            ["state"] = state,
        };
        return AuthorizeUrl(provider, query);
    }

    /// <summary>Completes an approved payment with the payer's token (from the code): the bank's status (CPLT when done) and its reference.</summary>
    public async Task<(string Status, string? Reference)> ExecutePaymentAsync(BankProvider provider, string secret, string code, string returnUrl, string paymentId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var payer = await TokenAsync(provider, secret, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = returnUrl, ["scope"] = "UserOAuth2Security",
        }, ct).ConfigureAwait(false);
        var answer = await SendAsync(HttpMethod.Post, provider, $"/v1/payments/{Uri.EscapeDataString(paymentId)}/execute", payer, null, new JsonObject(), ct).ConfigureAwait(false);
        return ((string?)answer?["code"] ?? "UNKNOWN", (string?)answer?["refNumber"]);
    }

    /// <summary>The sandbox sign helper sits next to the PSD2 API: …/sb/psd2 → …/sb/jwssignverifyapi/sign.</summary>
    private static string SignUrl(BankProvider provider)
    {
        var api = Base(provider);
        return (api.EndsWith("/psd2", StringComparison.OrdinalIgnoreCase) ? api[..^"/psd2".Length] : api) + "/jwssignverifyapi/sign";
    }

    /// <summary>The account id inside a connection's account uid ("subscription|account").</summary>
    public static string AccountOf(string accountUid) => Split(accountUid).Account;

    /// <summary>The copy has no call to end a subscription; it ends by its date. Nothing to do.</summary>
    public Task EndAsync(BankProvider provider, string privateKey, string sessionId, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// The account a connection keeps: the bank's account id. Reads need the subscription too, which the ERP holds as the session: the connection's
    /// account uid is "subscription|account" (see <see cref="AccountUid"/>).
    /// </summary>
    public static string AccountUid(string subscription, string account) => subscription + "|" + account;

    private static (string Subscription, string Account) Split(string accountUid)
    {
        var at = accountUid.IndexOf('|', StringComparison.Ordinal);
        return at > 0 ? (accountUid[..at], accountUid[(at + 1)..]) : throw new BankProviderException("Connect the account again: it was connected before Bank of Cyprus reads kept its subscription.");
    }

    private Task<string> AppTokenAsync(BankProvider provider, string secret, CancellationToken ct) =>
        TokenAsync(provider, secret, new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["scope"] = "TPPOAuth2Security" }, ct);

    private async Task<string> TokenAsync(BankProvider provider, string secret, Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = provider.ApplicationId;
        form["client_secret"] = secret;
        using var request = new HttpRequestMessage(HttpMethod.Post, Base(provider) + "/oauth2/token") { Content = new FormUrlEncodedContent(form) };
        var json = await ReadAsync(request, ct).ConfigureAwait(false);
        return (string?)json?["access_token"] ?? throw new BankProviderException("The bank gave no access token.");
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, BankProvider provider, string path, string token, string? subscription, JsonObject? body, CancellationToken ct,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, Base(provider) + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("journeyId", Guid.NewGuid().ToString());
        request.Headers.Add("timeStamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        if (subscription is not null)
        {
            request.Headers.Add("subscriptionId", subscription);
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (body is not null)
        {
            request.Content = Json(body);
        }

        return await ReadAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<JsonNode?> ReadAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new BankProviderException($"Bank of Cyprus can't be reached: {e.Message}");
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
                // The bank's errors: { code, description: [ … ] } (the copy); the sandbox's { error: { description, additionalDetails: [ { description } ] } }
                // or { httpMessage, moreInformation }; OAuth's { error, error_description }.
                var reason = Text(json?["error"]?["additionalDetails"]?[0]?["description"]) ?? Text(json?["moreInformation"])
                    ?? (json?["description"] is JsonArray d && d.Count > 0 ? string.Join("; ", d.Select(Text)) : Text(json?["description"]))
                    ?? Text(json?["error_description"]) ?? Text(json?["error"]?["description"]) ?? Text(json?["error"]) ?? response.ReasonPhrase ?? "refused";
                throw new BankProviderException($"Bank of Cyprus refused ({(int)response.StatusCode}): {reason}");
            }

            return json;
        }
    }

    /// <summary>
    /// The bank's sign-in address. The sandbox's login page sends redirect_uri back unencoded and its gateway then answers "badstate" unless the first
    /// request had it unencoded too, so ':' and '/' stay as they are there (valid in a query).
    /// </summary>
    private static string AuthorizeUrl(BankProvider provider, Dictionary<string, string> query) =>
        Base(provider) + "/oauth2/authorize?" + string.Join('&', query.Select(q => q.Key + "=" + (q.Key == "redirect_uri"
            ? Uri.EscapeDataString(q.Value).Replace("%3A", ":", StringComparison.Ordinal).Replace("%2F", "/", StringComparison.Ordinal)
            : Uri.EscapeDataString(q.Value))));

    /// <summary>A JSON body sent as plain "application/json": the sandbox's gateway answers 500 "Invalid" when a charset follows it.</summary>
    private static StringContent Json(JsonObject body)
    {
        var content = new StringContent(body.ToJsonString(), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    /// <summary>A JSON string's text; null for anything else (an object or array is not a reason).</summary>
    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim(' ', ':') : null;

    private static string Base(BankProvider provider) => provider.ApiBase.TrimEnd('/');

    private static DateOnly? Day(string? value) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}

/// <summary>The payer's side of a payment initiation, which Bank of Cyprus asks for: their address and their browser or app.</summary>
public sealed record BankPayer(string Ip, string Device);

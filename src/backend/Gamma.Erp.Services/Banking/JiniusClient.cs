using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Payments;

namespace Gamma.Erp.Services.Banking;

/// <summary>What the network answered: the HTTP status, the body (JSON when it is JSON) and its text.</summary>
public sealed record JiniusAnswer(HttpStatusCode Status, JsonNode? Body, string Text)
{
    public bool Ok => (int)Status is >= 200 and < 300;

    /// <summary>The network's own reason, else the status.</summary>
    public string Problem =>
        (string?)Body?["Detail"] ?? (string?)Body?["detail"] ?? (string?)Body?["title"] ?? (Text is { Length: > 0 } t ? (t.Length > 400 ? t[..400] : t) : $"HTTP {(int)Status}");
}

/// <summary>
/// The Jinius Integrations API v3 (the founder 2026-11-28): a client-credentials token (it lasts minutes, so it is kept until shortly before it ends, per
/// address and client) and plain JSON calls. It keeps nothing else; the services decide what to send and keep the answers.
/// </summary>
public sealed class JiniusClient(HttpClient http)
{
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Until)> Tokens = new(StringComparer.Ordinal);

    /// <summary>A token for the provider's client; the network's refusal is thrown with its reason.</summary>
    public async Task<string> TokenAsync(EInvoiceProvider provider, string secret, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var key = $"{provider.ApiBase}|{provider.ClientId}";
        if (Tokens.TryGetValue(key, out var kept) && kept.Until > DateTimeOffset.UtcNow)
        {
            return kept.Token;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{provider.ApiBase}/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials", ["client_id"] = provider.ClientId, ["client_secret"] = secret,
            }),
        };
        var answer = await ReadAsync(request, ct).ConfigureAwait(false);
        if (!answer.Ok || (string?)answer.Body?["access_token"] is not { Length: > 0 } token)
        {
            throw new InvalidOperationException($"Jinius refused the client's keys: {answer.Problem}");
        }

        var seconds = (int?)answer.Body?["expires_in"] ?? 300;
        Tokens[key] = (token, DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, seconds - 30)));
        return token;
    }

    /// <summary>A JSON call with the provider's token.</summary>
    public async Task<JiniusAnswer> CallAsync(EInvoiceProvider provider, string secret, HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        var token = await TokenAsync(provider, secret, ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(method, $"{provider.ApiBase}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        return await ReadAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>Attaches a PDF to an invoice (multipart, field "file").</summary>
    public async Task<JiniusAnswer> AttachPdfAsync(EInvoiceProvider provider, string secret, string invoiceId, byte[] pdf, string fileName, CancellationToken ct)
    {
        var token = await TokenAsync(provider, secret, ct).ConfigureAwait(false);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{provider.ApiBase}/v3/invoices/{Uri.EscapeDataString(invoiceId)}/attachPdf") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await ReadAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<JiniusAnswer> ReadAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.UserAgent.ParseAdd("GammaERP/1.0");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        JsonNode? body = null;
        if (text.TrimStart() is ['{', ..] or ['[', ..])
        {
            try
            {
                body = JsonNode.Parse(text);
            }
            catch (System.Text.Json.JsonException)
            {
                body = null;
            }
        }
        else if (text is "true" or "false")
        {
            body = JsonValue.Create(text == "true");
        }

        return new JiniusAnswer(response.StatusCode, body, text);
    }
}

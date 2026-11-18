using System.Globalization;
using System.Net;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Banking;

namespace Gamma.Erp.Api.Banking;

/// <summary>
/// The payment link pages (plan 4.5): <c>/pay/{payToken}</c> (the till's QR code; JSON for apps when asked with <c>Accept: application/json</c>),
/// <c>POST /pay/{payToken}/bank-of-cyprus</c> (starts the payment and sends the payer to the bank) and <c>/pay/bank-return</c> (the bank sends the payer back).
/// Nobody is signed in: the link's token is the only key, and the pages show no personal data.
/// </summary>
public static class PaymentLinkPages
{
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var pay = app.MapGroup("/pay").AllowAnonymous().DisableAntiforgery().ExcludeFromDescription();

        // The returns come first: "bank-return" and "card-return" are never tokens (tokens are 32 URL-safe characters).
        pay.MapGet("/bank-return", async (PaymentLinks links, HttpContext http, string? state, string? code, string? error, CancellationToken ct) =>
        {
            var back = await links.ReturnAsync(state, code, error, ct);
            return Page(http, back.Link is null ? "Payment" : back.Paid ? "Paid" : "Not paid", back.Link, back.Message, back.Paid);
        });

        pay.MapGet("/card-return", async (PaymentLinks links, HttpContext http, string? orderId, CancellationToken ct) =>
        {
            var back = await links.CardReturnAsync(orderId, ct);
            return Page(http, back.Link is null ? "Payment" : back.Paid ? "Paid" : "Not paid", back.Link, back.Message, back.Paid);
        });

        pay.MapGet("/{payToken}", async (PaymentLinks links, HttpContext http, string payToken, CancellationToken ct) =>
        {
            var view = await links.ViewAsync(payToken, ct);
            if (http.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return view is null ? Results.NotFound(new { message = "There is no such payment link." }) : Results.Ok(view);
            }

            return view is null ? Page(http, "Payment", null, "There is no such payment link.", false, 404)
                : Page(http, view.Status == "Paid" ? "Paid" : "Pay the bill", view, view.Status switch
                {
                    "Paid" => "Paid. Show this page at the till.",
                    "Expired" => "This payment link has expired: ask at the till for a new one.",
                    "Cancelled" => "This payment link was cancelled.",
                    _ => null,
                }, view.Status == "Paid");
        });

        pay.MapPost("/{payToken}/card", async (PaymentLinks links, HttpContext http, string payToken, CancellationToken ct) =>
        {
            try
            {
                return Results.Redirect(await links.StartCardAsync(payToken, ct), permanent: false, preserveMethod: false);
            }
            catch (Exception e) when (e is ValidationException or BankProviderException)
            {
                var view = await links.ViewAsync(payToken, ct);
                var reason = e is ValidationException v ? string.Join(" ", v.Errors.Values) : e.Message;
                return Page(http, "Not paid", view, reason, false, 422);
            }
        });

        pay.MapPost("/{payToken}/bank-of-cyprus", async (PaymentLinks links, HttpContext http, string payToken, CancellationToken ct) =>
        {
            try
            {
                return Results.Redirect(await links.StartBankOfCyprusAsync(payToken, ct), permanent: false, preserveMethod: false);
            }
            catch (Exception e) when (e is ValidationException or BankProviderException)
            {
                var view = await links.ViewAsync(payToken, ct);
                var reason = e is ValidationException v ? string.Join(" ", v.Errors.Values) : e.Message;
                return Page(http, "Not paid", view, reason, false, 422);
            }
        });
    }

    private static IResult Page(HttpContext http, string title, PaymentLinkView? link, string? message, bool paid, int status = 200)
    {
        // No scripts; forms post to the page itself, which then sends the payer on to the bank (https).
        http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self' https:; frame-ancestors 'none'; base-uri 'none'";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers.CacheControl = "no-store";
        var e = (string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var body = link is null ? string.Empty : $"""
            <p class="shop">{e(link.Shop)}</p>
            <p class="amount">{e(link.Currency)} {link.Amount.ToString("0.00", CultureInfo.InvariantCulture)}</p>
            <p class="muted">Bill {e(link.Reference)}{(paid && link.ProviderReference is { } r ? $" · bank reference {e(r)}" : string.Empty)}</p>
            """;
        var actions = link is not { Status: "Open" } ? string.Empty
            : link.Methods.Contains(PaymentLinks.Jcc) ? $"""<form method="post" action="/pay/{e(link.PayToken)}/card"><button type="submit">Pay by card</button></form>"""
            : link.Methods.Contains(PaymentLinks.BankOfCyprus) ? $"""<form method="post" action="/pay/{e(link.PayToken)}/bank-of-cyprus"><button type="submit">Pay with Bank of Cyprus</button></form>"""
            : string.Empty;
        var note = message is null ? string.Empty : $"""<p class="{(paid ? "paid" : "note")}">{e(message)}</p>""";
        var html = $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{e(title)}} | Gamma ERP</title>
            <style>
              body { font: 16px/1.5 system-ui, sans-serif; margin: 0; background: #f5f7fa; color: #1b2430; }
              main { max-width: 420px; margin: 0 auto; padding: 32px 16px; }
              .card { background: #fff; border-radius: 12px; padding: 24px; box-shadow: 0 1px 3px rgba(0,0,0,.08); }
              h1 { font-size: 20px; margin: 0 0 16px; } .shop { margin: 0; font-weight: 600; } .amount { font-size: 32px; font-weight: 700; margin: 8px 0; }
              .muted { color: #5b6675; margin: 0 0 16px; } .paid { color: #116b3a; font-weight: 600; } .note { color: #8a4b00; }
              button { width: 100%; padding: 14px; font: inherit; font-weight: 600; border: 0; border-radius: 8px; background: #003a70; color: #fff; cursor: pointer; }
              footer { color: #5b6675; font-size: 13px; margin-top: 16px; text-align: center; }
            </style></head>
            <body><main><div class="card"><h1>{{e(title)}}</h1>{{body}}{{note}}{{actions}}</div>
            <footer>Gamma ERP payment link · no personal data is shown here</footer></main></body></html>
            """;
        return Results.Content(html, "text/html; charset=utf-8", statusCode: status);
    }
}

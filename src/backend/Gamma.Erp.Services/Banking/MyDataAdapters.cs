using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Payments;

namespace Gamma.Erp.Services.Banking;

/// <summary>
/// Greece: AADE myDATA, SendInvoices (an InvoicesDoc with one invoice). The real message is XML; the simulator takes the same elements as JSON. The answer
/// gives the document's MARK, UID and the QR address. Payments by bank or card carry the bank's proof (transactionId).
/// </summary>
public class GreeceMyDataAdapter(HttpClient http) : IFiscalAdapter
{
    public virtual string Kind => "GreeceMyData";

    /// <summary>The issuer's country as myDATA wants it.</summary>
    protected virtual string Country => "GR";

    /// <summary>The route of the authority's API.</summary>
    protected virtual string Path => "/api/gr/invoices";

    public async Task<FiscalAnswer> DeclareAsync(FiscalProvider provider, string? certificate, FiscalSeries series, FiscalDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(document);
        var invoice = new JsonObject
        {
            ["issuer"] = new JsonObject { ["vatNumber"] = provider.IssuerTaxNumber, ["country"] = Country, ["branch"] = 0, ["name"] = document.SellerName },
            ["invoiceHeader"] = new JsonObject
            {
                ["series"] = series.SeriesCode, ["aa"] = document.Number.ToString(CultureInfo.InvariantCulture), ["issueDate"] = document.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["invoiceType"] = series.DocumentCode, ["currency"] = document.Currency,
            },
            ["paymentMethods"] = new JsonObject
            {
                ["paymentMethodDetails"] = new JsonArray([.. document.Payments.Select(p => (JsonNode)new JsonObject
                {
                    ["type"] = int.Parse(p.Code, CultureInfo.InvariantCulture), ["amount"] = p.Amount, ["transactionId"] = p.TransactionId,
                })]),
            },
            ["invoiceDetails"] = new JsonArray([.. document.Lines.Select(l => (JsonNode)new JsonObject
            {
                ["lineNumber"] = l.Number, ["quantity"] = l.Quantity, ["netValue"] = l.Net, ["vatCategory"] = int.Parse(l.VatCode, CultureInfo.InvariantCulture), ["vatAmount"] = l.Vat,
            })]),
            ["invoiceSummary"] = new JsonObject
            {
                ["totalNetValue"] = document.Net, ["totalVatAmount"] = document.Vat, ["totalWithheldAmount"] = 0, ["totalFeesAmount"] = 0, ["totalStampDutyAmount"] = 0,
                ["totalOtherTaxesAmount"] = 0, ["totalDeductionsAmount"] = 0, ["totalGrossValue"] = document.Gross,
            },
        };
        var body = new JsonObject { ["invoices"] = new JsonArray(invoice) };
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(provider.ApiBase.TrimEnd('/') + Path, body, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new FiscalAnswer(FiscalAnswer.NoAnswer, null, null, null, "The tax authority can't be reached: " + e.Message, null);
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
                // Not JSON: no answer to keep.
            }

            var row = json?["response"]?.AsArray().FirstOrDefault();
            if (response.IsSuccessStatusCode && (string?)row?["statusCode"] == "Success")
            {
                return new FiscalAnswer(FiscalAnswer.Confirmed, row!["invoiceMark"]?.ToString(), (string?)row["qrUrl"], (string?)row["invoiceUid"], null, json);
            }

            if ((int)response.StatusCode >= 500 || json is null)
            {
                return new FiscalAnswer(FiscalAnswer.NoAnswer, null, null, null, $"The tax authority answered {(int)response.StatusCode}.", json);
            }

            var errors = (row?["errors"] ?? json["errors"])?.AsArray().Select(e => $"{(string?)e?["field"]}: {(string?)e?["message"]}") ?? [];
            return new FiscalAnswer(FiscalAnswer.Refused, null, null, null, string.Join("; ", errors) is { Length: > 0 } text ? text : "Refused.", json);
        }
    }
}

/// <summary>
/// Cyprus declares in Greece's myDATA format (the founder, 2026-11-18: "use Cyprus the same as Greece"), to its own route; kept as its own adapter so it can
/// change later. The VAT categories (19, 9, 5, 3 %) come from the provider's codes like every country's.
/// </summary>
public sealed class CyprusMyDataAdapter(HttpClient http) : GreeceMyDataAdapter(http)
{
    public override string Kind => "CyprusMyData";

    protected override string Country => "CY";

    protected override string Path => "/api/cy/invoices";
}

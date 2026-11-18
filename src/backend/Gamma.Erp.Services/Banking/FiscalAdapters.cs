using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Payments;

namespace Gamma.Erp.Services.Banking;

/// <summary>What the three adapters below share: posting the JSON and reading the authority's answer (refusals with their fields).</summary>
public abstract class FiscalAdapterBase(HttpClient http)
{
    /// <summary>The time as written in a country's messages (date, time, offset), in the country's own time zone.</summary>
    protected static string LocalTime(DateTimeOffset at, string zone, string windowsZone)
    {
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
        }
        catch (TimeZoneNotFoundException)
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(windowsZone);
        }

        return TimeZoneInfo.ConvertTime(at, tz).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    protected static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A code of the provider's codes (e.g. Albania's operator code), or null.</summary>
    protected static string? Code(FiscalProvider provider, string name) => (string?)provider.Codes?[name];

    /// <summary>Posts the declaration; null with the answer when it was not taken, the JSON when it was answered.</summary>
    protected async Task<(JsonNode? Json, FiscalAnswer? Failed)> PostAsync(FiscalProvider provider, string path, JsonNode body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(provider.ApiBase.TrimEnd('/') + path, body, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (null, new FiscalAnswer(FiscalAnswer.NoAnswer, null, null, null, "The tax authority can't be reached: " + e.Message, null));
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
                // Not JSON.
            }

            if ((int)response.StatusCode >= 500 || json is null)
            {
                return (null, new FiscalAnswer(FiscalAnswer.NoAnswer, null, null, null, $"The tax authority answered {(int)response.StatusCode}.", json));
            }

            if (!response.IsSuccessStatusCode)
            {
                return (null, Refused(json, json["errors"]));
            }

            return (json, null);
        }
    }

    protected static FiscalAnswer Refused(JsonNode? json, JsonNode? errors)
    {
        var text = errors is JsonArray list ? string.Join("; ", list.Select(e => $"{(string?)e?["field"]}: {(string?)e?["message"]}"))
            : (string?)json?["message"] ?? (string?)json?["title"];
        return new FiscalAnswer(FiscalAnswer.Refused, null, null, null, string.IsNullOrWhiteSpace(text) ? "Refused." : text, json);
    }

    protected static FiscalAnswer NotConfigured(string what) =>
        new(FiscalAnswer.Refused, null, null, null, $"{ErrorCodes.NotConfiguredError}: {what}", null);

    /// <summary>An RSA private key in PEM form (the seller's certificate key); null when it is not one.</summary>
    protected static RSA? Key(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return null;
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException)
        {
            rsa.Dispose();
            return null;
        }
    }
}

/// <summary>
/// Albania: fiscalisation through CIS, RegisterInvoiceRequest. The IIC (NSLF) is the MD5 of the seller's RSA-SHA256 signature over
/// NIPT|issueDateTime|invNum|businUnitCode|tcrCode|softCode|totPrice; the answer is the FIC (NIVF). The till's TCR and business unit are the series' device
/// and unit codes; the software code is the provider's; the operator is the provider's codes.operator; payment types (BANKNOTE, CARD, ACCOUNT…) come from
/// codes.payment. The seller's key is the provider's certificate (PEM, sealed).
/// </summary>
public sealed class AlbaniaFiscalisationAdapter(HttpClient http) : FiscalAdapterBase(http), IFiscalAdapter
{
    private static readonly HashSet<string> CashTypes = ["BANKNOTE", "CARD", "CHECK", "SVOUCHER", "COMPANY", "ORDER"];

    public string Kind => "AlbaniaFiscalisation";

    public async Task<FiscalAnswer> DeclareAsync(FiscalProvider provider, string? certificate, FiscalSeries series, FiscalDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(document);
        var d = document;
        using var key = Key(certificate);
        if (key is null)
        {
            return NotConfigured("the seller's certificate key (PEM) is needed to make the IIC (fiscal provider, certificate).");
        }

        if (series.DeviceCode is not { Length: > 0 } tcr || series.UnitCode is not { Length: > 0 } unit || provider.SoftwareCode is not { Length: > 0 } soft
            || Code(provider, "operator") is not { Length: > 0 } op)
        {
            return NotConfigured("the series' TCR (device code) and business unit, the provider's software code and codes.operator are needed.");
        }

        var issued = LocalTime(d.IssuedOn, "Europe/Tirane", "Central Europe Standard Time");
        var year = issued[..4];
        var invNum = $"{d.Number}/{year}/{tcr}";
        var total = d.Gross;
        var signed = key.SignData(Encoding.UTF8.GetBytes($"{provider.IssuerTaxNumber}|{issued}|{invNum}|{unit}|{tcr}|{soft}|{Money(total)}"), HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var signature = Convert.ToHexString(signed);
#pragma warning disable CA5351 // Albania's IIC is defined as the MD5 of the signature (the law's format, not a security choice here).
        var iic = Convert.ToHexString(MD5.HashData(signed));
#pragma warning restore CA5351
        var cash = d.Payments.All(p => CashTypes.Contains(p.Code));
        var invoice = new JsonObject
        {
            ["typeOfInv"] = cash ? "CASH" : "NONCASH", ["isSimplifiedInv"] = false, ["issueDateTime"] = issued, ["invNum"] = invNum, ["invOrdNum"] = d.Number,
            ["tcrCode"] = tcr, ["isIssuerInVAT"] = true, ["totPriceWoVAT"] = d.Net, ["totVATAmt"] = d.Vat, ["totPrice"] = total, ["operatorCode"] = op, ["businUnitCode"] = unit,
            ["softCode"] = soft, ["iic"] = iic, ["iicSignature"] = signature, ["isReverseCharge"] = false,
            ["payMethods"] = new JsonArray([.. d.Payments.Select(p => (JsonNode)new JsonObject { ["type"] = p.Code, ["amt"] = p.Amount })]),
            ["currency"] = new JsonObject { ["code"] = d.Currency, ["exRate"] = 1, ["isBuying"] = false },
            ["seller"] = new JsonObject { ["idType"] = "NUIS", ["idNum"] = provider.IssuerTaxNumber, ["name"] = d.SellerName ?? provider.Name, ["country"] = "ALB" },
            ["items"] = new JsonArray([.. d.Lines.Select(l => (JsonNode)new JsonObject
            {
                ["n"] = l.Name ?? "Item", ["c"] = l.ProductCode, ["u"] = "C62", ["q"] = l.Quantity, ["upb"] = decimal.Round(l.Net / l.Quantity, 2), ["upa"] = decimal.Round((l.Net + l.Vat) / l.Quantity, 2),
                ["pb"] = l.Net, ["vr"] = l.VatRate, ["va"] = l.Vat, ["pa"] = l.Net + l.Vat,
            })]),
            ["sameTaxes"] = new JsonArray([.. d.Lines.GroupBy(l => l.VatRate).Select(g => (JsonNode)new JsonObject
            {
                ["numOfItems"] = g.Count(), ["priceBefVAT"] = g.Sum(l => l.Net), ["vatRate"] = g.Key, ["vatAmt"] = g.Sum(l => l.Vat),
            })]),
        };
        var body = new JsonObject
        {
            ["header"] = new JsonObject { ["uuid"] = Guid.NewGuid().ToString(), ["sendDateTime"] = LocalTime(DateTimeOffset.UtcNow, "Europe/Tirane", "Central Europe Standard Time") },
            ["invoice"] = invoice,
        };
        var (json, failed) = await PostAsync(provider, "/api/al/invoices", body, ct).ConfigureAwait(false);
        if (failed is not null)
        {
            return failed;
        }

        var fic = (string?)json!["fic"];
        var qr = $"{provider.ApiBase.TrimEnd('/')}/invoice-check/#/verify?iic={iic}&tin={provider.IssuerTaxNumber}&crtd={Uri.EscapeDataString(issued)}&ord={d.Number}"
            + $"&bu={unit}&cr={tcr}&sw={soft}&prc={Money(total)}";
        return fic is null ? Refused(json, json["errors"]) : new FiscalAnswer(FiscalAnswer.Confirmed, fic, qr, iic, null, json);
    }
}

/// <summary>
/// Spain: VERI*FACTU, RegFactuSistemaFacturacion with one RegistroAlta. Each record's huella is the SHA-256 of its fields and the previous record's huella
/// (the chain per seller); the answer is the CSV. The invoice type is the series' document code (F2 = simplified, a till receipt); each VAT group's code is
/// its calificación (S1 taxed, N1 / N2 not subject); the installation number is the series' device code.
/// </summary>
public sealed class SpainVeriFactuAdapter(HttpClient http) : FiscalAdapterBase(http), IFiscalAdapter
{
    public string Kind => "SpainVeriFactu";

    public async Task<FiscalAnswer> DeclareAsync(FiscalProvider provider, string? certificate, FiscalSeries series, FiscalDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(document);
        var d = document;
        var nif = provider.IssuerTaxNumber;
        var number = $"{series.SeriesCode}-{d.Number}";
        var date = d.IssueDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

        // The VAT breakdown by rate and calificación: the quota of each group is its base × rate, rounded once.
        var desglose = d.Lines.GroupBy(l => (l.VatRate, l.VatCode)).Select(g =>
        {
            var taxed = g.Key.VatCode is "S1" or "S2";
            var net = g.Sum(l => l.Net);
            return (Taxed: taxed, Rate: g.Key.VatRate, Code: g.Key.VatCode, Base: net, Quota: taxed ? decimal.Round(net * g.Key.VatRate / 100m, 2, MidpointRounding.AwayFromZero) : 0m);
        }).ToList();
        var quota = desglose.Sum(x => x.Quota);
        var total = desglose.Sum(x => x.Base + x.Quota);
        var generated = LocalTime(DateTimeOffset.UtcNow, "Europe/Madrid", "Romance Standard Time");
        var previous = series.LastHash;
        var previousNumber = $"{series.SeriesCode}-{series.LastNumber}";
        var previousDate = series.LastIssuedOn is { } last ? DateOnly.FromDateTime(last.UtcDateTime).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) : date;
        var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"IDEmisorFactura={nif}&NumSerieFactura={number}&FechaExpedicionFactura={date}&TipoFactura={series.DocumentCode}&CuotaTotal={Money(quota)}"
            + $"&ImporteTotal={Money(total)}&Huella={previous ?? string.Empty}&FechaHoraHusoGenRegistro={generated}")));
        var alta = new JsonObject
        {
            ["idVersion"] = "1.0",
            ["idFactura"] = new JsonObject { ["idEmisorFactura"] = nif, ["numSerieFactura"] = number, ["fechaExpedicionFactura"] = date },
            ["nombreRazonEmisor"] = d.SellerName ?? provider.Name, ["tipoFactura"] = series.DocumentCode, ["descripcionOperacion"] = "Venta " + d.BillCode,
            ["desglose"] = new JsonArray([.. desglose.Select(x => (JsonNode)new JsonObject
            {
                ["impuesto"] = "01", ["claveRegimen"] = "01", ["calificacionOperacion"] = x.Code, ["tipoImpositivo"] = x.Taxed ? x.Rate : null,
                ["baseImponibleOimporteNoSujeto"] = x.Base, ["cuotaRepercutida"] = x.Taxed ? x.Quota : null,
            })]),
            ["cuotaTotal"] = quota, ["importeTotal"] = total,
            ["encadenamiento"] = previous is null ? new JsonObject { ["primerRegistro"] = "S" }
                : new JsonObject { ["registroAnterior"] = new JsonObject { ["idEmisorFactura"] = nif, ["numSerieFactura"] = previousNumber, ["fechaExpedicionFactura"] = previousDate, ["huella"] = previous } },
            ["sistemaInformatico"] = new JsonObject
            {
                ["nombreRazon"] = "Gamma ERP", ["nif"] = nif, ["nombreSistemaInformatico"] = "Gamma ERP", ["idSistemaInformatico"] = provider.SoftwareCode ?? "GE", ["version"] = "1.0",
                ["numeroInstalacion"] = series.DeviceCode ?? "1", ["tipoUsoPosibleSoloVerifactu"] = "S", ["tipoUsoPosibleMultiOT"] = "N", ["indicadorMultiplesOT"] = "N",
            },
            ["fechaHoraHusoGenRegistro"] = generated, ["tipoHuella"] = "01", ["huella"] = huella,
        };
        var body = new JsonObject
        {
            ["cabecera"] = new JsonObject { ["obligadoEmision"] = new JsonObject { ["nombreRazon"] = d.SellerName ?? provider.Name, ["nif"] = nif } },
            ["registroFactura"] = new JsonArray(new JsonObject { ["registroAlta"] = alta }),
        };
        var (json, failed) = await PostAsync(provider, "/api/es/records", body, ct).ConfigureAwait(false);
        if (failed is not null)
        {
            return failed;
        }

        var line = json!["respuestaLinea"]?.AsArray().FirstOrDefault();
        if ((string?)json["status"] != "OK" || (string?)line?["estadoRegistro"] != "Correcto")
        {
            return Refused(json, line?["errors"]);
        }

        var qr = $"{provider.ApiBase.TrimEnd('/')}/qr/es?nif={nif}&numserie={Uri.EscapeDataString(number)}&fecha={date}&importe={Money(total)}";
        return new FiscalAnswer(FiscalAnswer.Confirmed, (string?)json["csv"], qr, huella, null, json);
    }
}

/// <summary>
/// Portugal: AT e-Fatura, RegisterInvoice. The invoice is "type series/number" with the series' ATCUD validation code; it is signed (RSA-SHA1 over
/// InvoiceDate;SystemEntryDate;InvoiceNo;GrossTotal;previous signature) with the certified software's key (the provider's certificate), and its 4 hash
/// characters are printed. The invoice type is the series' document code (FS = simplified); a VAT group's code is its exemption code when the rate is 0;
/// the tax region is codes.region (PT, PT-AC, PT-MA).
/// </summary>
public sealed class PortugalAtcudAdapter(HttpClient http) : FiscalAdapterBase(http), IFiscalAdapter
{
    public string Kind => "PortugalAtcud";

    public async Task<FiscalAnswer> DeclareAsync(FiscalProvider provider, string? certificate, FiscalSeries series, FiscalDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(document);
        var d = document;
        using var key = Key(certificate);
        if (key is null)
        {
            return NotConfigured("the certified software's key (PEM) is needed to sign the document (fiscal provider, certificate).");
        }

        if (series.ValidationCode is not { Length: >= 8 } validation)
        {
            return NotConfigured("the series' ATCUD validation code (8 or more letters and digits) is needed.");
        }

        var type = series.DocumentCode;
        var invoiceNo = $"{type} {series.SeriesCode}/{d.Number}";
        var atcud = $"{validation}-{d.Number}";
        var region = Code(provider, "region") ?? "PT";
        var entry = LocalTime(d.IssuedOn, "Europe/Lisbon", "GMT Standard Time")[..19];
        var gross = d.Gross;
#pragma warning disable CA5350 // Portugal's document signature is RSA-SHA1 by the AT's rules (Portaria 363/2010).
        var signature = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(
            $"{d.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)};{entry};{invoiceNo};{Money(gross)};{d.PreviousHash ?? string.Empty}"), HashAlgorithmName.SHA1,
            RSASignaturePadding.Pkcs1));
#pragma warning restore CA5350
        var hash4 = string.Concat(signature[0], signature[10], signature[20], signature[30]);
        var body = new JsonObject
        {
            ["taxRegistrationNumber"] = provider.IssuerTaxNumber, ["invoiceNo"] = invoiceNo, ["atcud"] = atcud, ["invoiceDate"] = d.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["invoiceType"] = type, ["invoiceStatus"] = "N", ["customerTaxID"] = "999999990", ["customerTaxIDCountry"] = "PT",
            ["line"] = new JsonArray([.. d.Lines.Select(l => (JsonNode)new JsonObject
            {
                ["creditAmount"] = l.Net, ["tax"] = new JsonObject { ["taxType"] = "IVA", ["taxCountryRegion"] = region, ["taxPercentage"] = l.VatRate },
                ["taxExemptionCode"] = l.VatRate == 0 ? l.VatCode : null, ["taxExemptionReason"] = l.VatRate == 0 ? "Isento" : null,
            })]),
            ["documentTotals"] = new JsonObject { ["taxPayable"] = d.Vat, ["netTotal"] = d.Net, ["grossTotal"] = gross },
            ["hashCharacters"] = hash4, ["softwareCertificateNumber"] = provider.CertificateNumber, ["paymentReference"] = d.Payments.Count > 0 ? d.Payments[0].TransactionId : null,
        };
        var (json, failed) = await PostAsync(provider, "/api/pt/documents", body, ct).ConfigureAwait(false);
        if (failed is not null)
        {
            return failed;
        }

        if ((string?)json!["status"] != "OK")
        {
            return Refused(json, json["errors"]);
        }

        // The AT's QR text (Portaria 195/2020), shortened to what the bill holds here.
        var qr = $"A:{provider.IssuerTaxNumber}*B:999999990*C:PT*D:{type}*E:N*F:{d.IssueDate:yyyyMMdd}*G:{invoiceNo}*H:{atcud}*I1:{region}*I7:{Money(d.Net)}*I8:{Money(d.Vat)}"
            + $"*N:{Money(d.Vat)}*O:{Money(gross)}*Q:{hash4}*R:{provider.CertificateNumber}";
        return new FiscalAnswer(FiscalAnswer.Confirmed, atcud, qr, signature, null, json);
    }
}

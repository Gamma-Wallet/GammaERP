using System.Text.Json.Nodes;
using Gamma.Erp.Models.Payments;

namespace Gamma.Erp.Services.Banking;

/// <summary>A bill line as declared: net and VAT in the company's currency, rounded to cents, with the authority's VAT code from the provider's codes.</summary>
public sealed record FiscalLine(int Number, string? ProductCode, string? Name, decimal Quantity, decimal Net, decimal VatRate, decimal Vat, string VatCode);

/// <summary>How the bill was paid: the authority's payment code (from the provider's codes), the amount, and the bank's or card's proof.</summary>
public sealed record FiscalPayment(string Code, decimal Amount, string? TransactionId);

/// <summary>What an adapter declares: the bill, its number in the series, the previous record's hash (chained countries), the seller.</summary>
public sealed record FiscalDocument(string BillCode, int Number, DateOnly IssueDate, DateTimeOffset IssuedOn, string Currency, string SellerTaxNumber, string? SellerName,
    IReadOnlyList<FiscalLine> Lines, IReadOnlyList<FiscalPayment> Payments, string? PreviousHash)
{
    public decimal Net => Lines.Sum(l => l.Net);

    public decimal Vat => Lines.Sum(l => l.Vat);

    public decimal Gross => Net + Vat;
}

/// <summary>The authority's answer: Confirmed (with its code, the QR text and the record's hash), Refused (with the reasons), or NoAnswer.</summary>
public sealed record FiscalAnswer(string Status, string? AuthorityCode, string? QrText, string? Hash, string? Errors, JsonNode? Raw)
{
    public const string Confirmed = "Confirmed";
    public const string Refused = "Refused";
    public const string NoAnswer = "NoAnswer";
}

/// <summary>
/// An adapter to one country's tax authority (plan 4.3c): it builds the country's declaration from the bill and sends it to the provider's address. It keeps
/// nothing; the engine (<see cref="FiscalReceipts"/>) moves the series and keeps the answer.
/// </summary>
public interface IFiscalAdapter
{
    /// <summary>The provider kind it serves (<see cref="FiscalProvider.Kind"/>).</summary>
    string Kind { get; }

    Task<FiscalAnswer> DeclareAsync(FiscalProvider provider, string? certificate, FiscalSeries series, FiscalDocument document, CancellationToken ct);
}

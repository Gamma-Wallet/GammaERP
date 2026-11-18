using System.Text.Json.Nodes;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Banking;

/// <summary>A declared bill as the till shows it: its state, the authority's code, the fiscal QR (text and SVG), and why it was refused.</summary>
public sealed record FiscalReceiptDto(Guid Id, Guid DocumentId, string? Bill, string Status, int? Number, string? AuthorityCode, string? QrText, string? QrSvg, string? Errors,
    int Attempts);

/// <summary>
/// Declaring bills to the tax authority (plan 4.3c, confirmation 2): only after the payment is confirmed (the cashier's Paid on a bank or card notice or a
/// Check bank line), only for document types marked Fiscalise, through the company's fiscal provider and the series of the point of sale. The number and
/// the chain move only when the authority confirms, in the same save as the receipt; refused or unanswered, the payment stays posted and the till shows Send
/// again. Nothing retries by itself. Codes (VAT categories, payment types) come from the provider row, never from code.
/// </summary>
public sealed class FiscalReceipts(ErpDbContext db, Visibility visibility, ISecretBox secrets, IEnumerable<IFiscalAdapter> adapters, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    /// <summary>Declares the bills a confirmed payment paid; bills of types not marked Fiscalise, or of a company without a provider, are left alone.</summary>
    public async Task<IReadOnlyList<FiscalReceiptDto>> DeclareAsync(IReadOnlyList<Guid> billIds, Guid pointOfSaleId, Guid paymentMethodId, Guid? statementLineId,
        string? paymentReference, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(billIds);
        var done = new List<FiscalReceiptDto>();
        foreach (var billId in billIds.Distinct())
        {
            var bill = await db.Set<Document>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == billId && d.Posted, ct).ConfigureAwait(false);
            if (bill is null || !await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == bill.TypeId && t.Fiscalise, ct).ConfigureAwait(false))
            {
                continue;
            }

            var provider = await db.Set<FiscalProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == bill.CompanyId, ct).ConfigureAwait(false);
            if (provider is null)
            {
                continue;
            }

            var receipt = await db.Set<FiscalReceipt>().FirstOrDefaultAsync(r => r.Active && r.DocumentId == billId, ct).ConfigureAwait(false);
            if (receipt is { Status: "Confirmed" })
            {
                done.Add(await DtoAsync(receipt, ct).ConfigureAwait(false));
                continue;
            }

            var series = await db.Set<FiscalSeries>().FirstOrDefaultAsync(s => s.Active && s.FiscalProviderId == provider.Id && s.CostCenterId == pointOfSaleId, ct)
                .ConfigureAwait(false);
            if (receipt is null)
            {
                receipt = new FiscalReceipt
                {
                    TenantId = bill.TenantId, OwnerId = bill.CompanyId, DocumentId = billId, FiscalProviderId = provider.Id, FiscalSeriesId = series?.Id ?? Guid.Empty,
                    BankStatementLineId = statementLineId, PaymentMethodId = paymentMethodId, PaymentReference = paymentReference,
                };
                if (series is null)
                {
                    (receipt.Status, receipt.Errors) = ("Refused", $"{ErrorCodes.NotConfiguredError}: the point of sale has no fiscal series (Configuration › Fiscal series).");
                    done.Add(await DtoAsync(receipt, ct).ConfigureAwait(false));
                    continue;
                }

                db.Add(receipt);
            }

            await SendAsync(receipt, bill, provider, series!, ct).ConfigureAwait(false);
            done.Add(await DtoAsync(receipt, ct).ConfigureAwait(false));
        }

        return done;
    }

    /// <summary>Send again (a person's press): a refused or unanswered receipt is declared again with the same payment.</summary>
    public async Task<FiscalReceiptDto> SendAgainAsync(Guid receiptId, CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var receipt = await db.Set<FiscalReceipt>().FirstOrDefaultAsync(r => r.Active && r.Id == receiptId, ct).ConfigureAwait(false);
        if (receipt is null || receipt.OwnerId is not { } company || !nodes.Contains(company))
        {
            throw ValidationException.For("receiptId", "There is no such fiscal receipt.");
        }

        if (receipt.Status == "Confirmed")
        {
            return await DtoAsync(receipt, ct).ConfigureAwait(false);
        }

        var bill = await db.Set<Document>().AsNoTracking().FirstAsync(d => d.Id == receipt.DocumentId, ct).ConfigureAwait(false);
        var provider = await db.Set<FiscalProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.Id == receipt.FiscalProviderId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("provider", "The company's fiscal provider is switched off.");
        var series = await db.Set<FiscalSeries>().FirstOrDefaultAsync(s => s.Active && s.Id == receipt.FiscalSeriesId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("series", $"{ErrorCodes.NotConfiguredError}: the receipt's fiscal series is switched off.");
        await SendAsync(receipt, bill, provider, series, ct).ConfigureAwait(false);
        return await DtoAsync(receipt, ct).ConfigureAwait(false);
    }

    /// <summary>The bills' receipts, for the till.</summary>
    public async Task<IReadOnlyList<FiscalReceiptDto>> OfBillsAsync(IReadOnlyList<Guid> billIds, CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var rows = await db.Set<FiscalReceipt>().AsNoTracking().Where(r => r.Active && billIds.Contains(r.DocumentId) && r.OwnerId != null && nodes.Contains(r.OwnerId.Value))
            .ToListAsync(ct).ConfigureAwait(false);
        var list = new List<FiscalReceiptDto>();
        foreach (var r in rows)
        {
            list.Add(await DtoAsync(r, ct).ConfigureAwait(false));
        }

        return list;
    }

    /// <summary>Builds the declaration and sends it; confirmed, the number and the chain move in the same save.</summary>
    private async Task SendAsync(FiscalReceipt receipt, Document bill, FiscalProvider provider, FiscalSeries series, CancellationToken ct)
    {
        var adapter = adapters.FirstOrDefault(a => string.Equals(a.Kind, provider.Kind, StringComparison.OrdinalIgnoreCase));
        var now = time.GetUtcNow();
        (receipt.Attempts, receipt.DeclaredOn) = (receipt.Attempts + 1, now);
        var (document, problem) = await DocumentAsync(bill, provider, series, receipt, ct).ConfigureAwait(false);
        if (adapter is null || document is null)
        {
            (receipt.Status, receipt.Errors) = ("Refused", adapter is null ? $"{ErrorCodes.NotConfiguredError}: the ERP has no adapter for {provider.Kind}." : problem);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var certificate = provider.Certificate is { Length: > 0 } sealedCert ? secrets.Open(sealedCert) : null;
        var answer = await adapter.DeclareAsync(provider, certificate, series, document, ct).ConfigureAwait(false);
        receipt.Answer = answer.Raw;
        if (answer.Status == FiscalAnswer.Confirmed)
        {
            (receipt.Status, receipt.Number, receipt.Hash, receipt.AuthorityCode, receipt.QrText, receipt.Errors, receipt.ConfirmedOn) =
                ("Confirmed", document.Number, answer.Hash, answer.AuthorityCode, answer.QrText, null, now);
            // The chain keeps the last record's number, hash and issue date (Spain's registroAnterior names its date).
            (series.LastNumber, series.LastHash, series.LastIssuedOn) = (document.Number, answer.Hash ?? series.LastHash,
                new DateTimeOffset(document.IssueDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        }
        else
        {
            (receipt.Status, receipt.Errors) = (answer.Status == FiscalAnswer.Refused ? "Refused" : "Declared", answer.Errors);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The bill as the adapter declares it, or why it can't be (a VAT group or payment method without its authority code).</summary>
    private async Task<(FiscalDocument? Document, string? Problem)> DocumentAsync(Document bill, FiscalProvider provider, FiscalSeries series, FiscalReceipt receipt,
        CancellationToken ct)
    {
        var lines = await db.Set<Transaction>().AsNoTracking().Where(t => t.Active && t.DocumentId == bill.Id && t.VatGroupId != null && t.ValueLcu != null)
            .OrderBy(t => t.CreatedOn).ToListAsync(ct).ConfigureAwait(false);
        var groups = await db.Set<VatGroup>().AsNoTracking().Where(g => lines.Select(l => l.VatGroupId).Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct).ConfigureAwait(false);
        var vatCodes = provider.Codes?["vat"] as JsonObject;
        var payCodes = provider.Codes?["payment"] as JsonObject;
        var fiscalLines = new List<FiscalLine>();
        foreach (var (line, n) in lines.Select((l, i) => (l, i + 1)))
        {
            var group = groups.GetValueOrDefault(line.VatGroupId!.Value);
            if ((string?)vatCodes?[line.VatGroupId.ToString()!] is not { } code)
            {
                return (null, $"{ErrorCodes.NotConfiguredError}: the VAT group {group?.Code ?? line.VatGroupId.ToString()} has no code for the tax authority (fiscal provider, codes.vat).");
            }

            var rate = group?.Rate ?? 0m;
            var net = decimal.Round(line.ValueLcu!.Value, 2, MidpointRounding.AwayFromZero);
            fiscalLines.Add(new FiscalLine(n, null, line.ElementName, line.Quantity ?? 1, net, rate, decimal.Round(net * rate / 100m, 2, MidpointRounding.AwayFromZero), code));
        }

        if (fiscalLines.Count == 0)
        {
            return (null, "The bill has no lines to declare.");
        }

        if (receipt.PaymentMethodId is not { } method || (string?)payCodes?[method.ToString()] is not { } payCode)
        {
            return (null, $"{ErrorCodes.NotConfiguredError}: the payment method has no code for the tax authority (fiscal provider, codes.payment).");
        }

        var company = await db.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == bill.CompanyId, ct).ConfigureAwait(false);
        var currency = await db.Set<Gamma.Erp.Models.Currencies.Currency>().AsNoTracking().Where(c => c.Active && c.IsBase && c.OwnerId == bill.CompanyId).Select(c => c.Code)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "EUR";
        var gross = fiscalLines.Sum(l => l.Net + l.Vat);
        var day = bill.DocumentDate ?? bill.PostingDate ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return (new FiscalDocument(bill.Code ?? bill.Id.ToString(), series.LastNumber + 1, day, time.GetUtcNow(), currency, provider.IssuerTaxNumber,
            company?.Name.In(Models.Base.TranslatedText.DefaultLanguage), fiscalLines, [new FiscalPayment(payCode, gross, receipt.PaymentReference)], series.LastHash), null);
    }

    private async Task<FiscalReceiptDto> DtoAsync(FiscalReceipt r, CancellationToken ct)
    {
        var bill = await db.Set<Document>().AsNoTracking().Where(d => d.Id == r.DocumentId).Select(d => d.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new FiscalReceiptDto(r.Id, r.DocumentId, bill, r.Status, r.Number, r.AuthorityCode, r.QrText, r.QrText is { Length: > 0 } qr ? PosBankPayments.Svg(qr) : null,
            r.Errors, r.Attempts);
    }
}

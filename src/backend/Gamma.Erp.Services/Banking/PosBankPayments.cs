using System.Globalization;
using System.Text;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace Gamma.Erp.Services.Banking;

/// <summary>The payment QR code a till shows: the bank account, the amount, the bill's code as the reference, the EPC text and its picture (SVG).</summary>
/// <para>PayToken: the ERP's payment request behind a link method (the till watches it); Note: why Gamma did not take an open bill ("Pay with Gamma app").</para>
public sealed record BankQrDto(Guid BankId, string? Bank, string Iban, string? Bic, string Holder, decimal Amount, string Currency, string Reference, string Payload, string Svg,
    bool CanCheck, string? Provider = null, string? PayToken = null, string? Note = null);

/// <summary>What Check bank looks for: the method's bank at the point of sale, the amount and the bill's code.</summary>
public sealed record BankCheckRequest(Guid PointOfSaleId, Guid PaymentMethodId, decimal Amount, string Reference);

/// <summary>Paying bills by a bank line the cashier chose after Check bank: the till's usual payment, and that line.</summary>
/// <para>Open: the bill Gamma holds open ("Pay with Gamma app"), set by the confirmation that pays it; it is then marked paid at Gamma instead of created.</para>
public sealed record PayByBankRequest(Gamma.Erp.Services.Pos.PayBillsRequest Pay, BankPaymentLine BankLine, OpenGammaBill? Open = null);

/// <summary>A money-in line from the bank that may be the payment: the newest first, those that mention the reference before the others.</summary>
public sealed record BankPaymentLine(string? ExternalId, DateOnly Date, decimal Amount, string? Text, string? Reference, string? CounterpartyName, string? CounterpartyIban,
    bool Booked, bool MentionsReference, string Kind = "bank");

/// <summary>
/// "Pay by bank" at the till (the founder, 2026-11-14): a payment method marked <see cref="PaymentMethod.ShowsBankQr"/> shows an EPC QR code (EPC069-12,
/// version 002, euro only) for the bank its link at the point of sale names. The payer scans it with their banking app; the cashier then presses
/// Check bank (one read of the bank, started by them) and Paid on the right line, which posts the receipt and matches that bank line in the same press.
/// Without a connection the cashier checks by eye and presses Received; the bank line is then matched later on Bank statements.
/// </summary>
public sealed class PosBankPayments(ErpDbContext db, Visibility visibility, BankConnections connections, PaymentLinks links, RequestContext request, ISecretBox secrets,
    GammaIntegrationClient gamma, TimeProvider? clock = null)
{
    /// <summary>How far back Check bank looks.</summary>
    public const int CheckMinutes = 60;

    private const string Euro = "EUR";

    private readonly TimeProvider time = clock ?? TimeProvider.System;

    /// <summary>The QR code for paying an amount; null when the method shows none.</summary>
    public async Task<BankQrDto?> QrAsync(Guid pointOfSaleId, Guid paymentMethodId, decimal amount, string reference, CancellationToken ct)
    {
        var method = await db.Set<PaymentMethod>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == paymentMethodId && m.Active, ct).ConfigureAwait(false);
        // A method paying through Bank of Cyprus shows the ERP's payment link; a "shows a bank QR" method the EPC transfer QR.
        var viaLink = PaymentLinks.ByLink(method?.ProviderKind);
        if (method is null || (!method.ShowsBankQr && !viaLink))
        {
            return null;
        }

        var (bankId, bank, connection) = await BankAsync(pointOfSaleId, paymentMethodId, ct).ConfigureAwait(false);
        if (connection?.Iban is not { } iban)
        {
            throw ValidationException.For("bankId", $"{ErrorCodes.NotConfiguredError}: the bank account {bank} has no IBAN. Type it on Bank statements, Bank accounts.");
        }

        await EuroAsync(ct).ConfigureAwait(false);
        if (amount <= 0 || amount > 999_999_999.99m || decimal.Round(amount, 2) != amount)
        {
            throw ValidationException.For("amount", "The amount is above 0, in cents, at most 999,999,999.99.");
        }

        var text = Clean(reference, 140) ?? throw ValidationException.For("reference", "The payment needs a reference: the bill's code.");
        var holder = Clean(connection.HolderName, 70) ?? Clean(await CompanyNameAsync(ct).ConfigureAwait(false), 70)
            ?? throw ValidationException.For("holderName", "The bank account has no holder: type it on Bank statements, Bank accounts.");
        if (viaLink)
        {
            var company = await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("pointOfSaleId", "The point of sale belongs to no company.");
            var (link, url) = await links.ForTillAsync(request.TenantId, company, pointOfSaleId, paymentMethodId, bankId, amount, Euro, text, method!.ProviderKind!, ct).ConfigureAwait(false);
            if (method.ProviderKind == PaymentLinks.GammaApp)
            {
                // Pay with Gamma app (plan 10): the bill goes to Gamma open; its QR is Gamma's (the till's Retailer app shows the same).
                var (gammaLink, note) = await OpenAtGammaAsync(link, company, pointOfSaleId, iban, holder, connection.Bic, ct).ConfigureAwait(false);
                return new BankQrDto(bankId, bank, iban, connection.Bic, holder, amount, Euro, text, gammaLink ?? string.Empty, gammaLink is null ? string.Empty : Svg(gammaLink),
                    CanCheck: true, PaymentLinks.GammaApp, link.PayToken, note);
            }

            return new BankQrDto(bankId, bank, iban, connection.Bic, holder, amount, Euro, text, url, Svg(url), CanCheck: true, method.ProviderKind, link.PayToken);
        }

        var payload = Payload(connection.Bic, holder, iban, amount, text);
        return new BankQrDto(bankId, bank, iban, connection.Bic, holder, amount, Euro, text, payload, Svg(payload), connection.SessionId is not null && connection.AccountUid is not null);
    }

    /// <summary>
    /// Sends the open bill of a "Pay with Gamma app" request to Gamma (once: a request that has Gamma's bill keeps it), with the shop's account, the point
    /// of sale and the staff login of its Retailer app (point of sale settings). Gamma's refusal is kept on the request and shown; choosing the method
    /// again sends it again.
    /// </summary>
    private async Task<(string? Link, string? Note)> OpenAtGammaAsync(Models.Payments.PaymentLink link, Guid company, Guid pointOfSaleId, string iban, string holder, string? bic,
        CancellationToken ct)
    {
        if (link.GammaBillId is not null)
        {
            return (link.GammaLink, null);
        }

        var provider = await db.Set<WalletProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("paymentMethodId", $"{ErrorCodes.NotConfiguredError}: connect the company to Gamma Wallet first (Configuration › Open banking, Gamma Wallet).");
        var (_, chain) = await Pos.PosPlaces.ChainAsync(db, await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false), pointOfSaleId, ct).ConfigureAwait(false);
        var staff = chain.Select(r => r.GammaStaffLogin?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v))
            ?? throw ValidationException.For("pointOfSaleId", $"{ErrorCodes.NotConfiguredError}: name the Gamma staff login of this point of sale's Retailer app (point of sale settings, Gamma staff login).");
        var place = await db.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == pointOfSaleId, ct).ConfigureAwait(false);
        var token = secrets.Open(provider.Token);
        if (token is null)
        {
            const string unreadable = "The Gamma Wallet token can no longer be opened: an administrator enters it again.";
            await links.KeepGammaAsync(link.Id, null, unreadable, ct).ConfigureAwait(false);
            return (null, unreadable);
        }

        try
        {
            var bill = await gamma.OpenBillAsync(provider.ApiBase, token, new GammaOpenBill(link.Reference, decimal.Round(link.Amount, 2), link.Currency, time.GetUtcNow(), iban, holder, bic,
                place?.Code, place?.Name.In(Models.Base.TranslatedText.DefaultLanguage), staff, link.ExpiresOn), GammaWalletBills.Version, ct).ConfigureAwait(false);
            await links.KeepGammaAsync(link.Id, bill, null, ct).ConfigureAwait(false);
            return (bill.Link, null);
        }
        catch (BankProviderException e)
        {
            await links.KeepGammaAsync(link.Id, null, e.Message, ct).ConfigureAwait(false);
            return (null, e.Message);
        }
    }

    /// <summary>
    /// Reads the bank once, for the cashier: the money-in lines of this amount from the last hour (booked or still pending) that no statement line holds
    /// yet, those mentioning the reference first.
    /// </summary>
    public async Task<IReadOnlyList<BankPaymentLine>> CheckAsync(Guid pointOfSaleId, Guid paymentMethodId, decimal amount, string reference, PsuPresence psu, CancellationToken ct)
    {
        var (bankId, _, _) = await BankAsync(pointOfSaleId, paymentMethodId, ct).ConfigureAwait(false);
        var ready = await connections.ReadyForPointOfSaleAsync(bankId, ct).ConfigureAwait(false);
        var now = time.GetLocalNow();
        var today = DateOnly.FromDateTime(now.DateTime);
        var lines = new List<BankTransactionInfo>();
        string? next = null;
        var pages = 0;
        do
        {
            var page = await BankConnections.Call(() => ready.Connector.TransactionsAsync(ready.Provider, ready.Key, ready.Connection.AccountUid!, today.AddDays(-1), today, next, psu, ct))
                .ConfigureAwait(false);
            lines.AddRange(page.Lines);
            next = page.Next;
        }
        while (next is not null && ++pages < 20);

        // Banks give days, not times: the lines of today (and yesterday, in the hour after midnight).
        var since = DateOnly.FromDateTime(now.AddMinutes(-CheckMinutes).DateTime);
        var statements = db.Set<BankStatement>().Where(s => s.Active && s.BankId == bankId).Select(s => s.Id);
        var held = (await db.Set<BankStatementLine>().AsNoTracking().Where(l => l.Active && l.ExternalId != null && statements.Contains(l.StatementId)).Select(l => l.ExternalId!)
            .ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var text = reference.Trim();
        return [.. lines.Where(l => l.Amount == amount && l.Date >= since && (l.ExternalId is null || !held.Contains(l.ExternalId)))
            .Select(l => new BankPaymentLine(l.ExternalId, l.Date, l.Amount, l.Text, l.Reference, l.CounterpartyName, BankConnections.Compact(l.CounterpartyIban), l.Booked,
                text.Length > 0 && ((l.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) || (l.Reference?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))))
            .OrderByDescending(l => l.MentionsReference).ThenByDescending(l => l.Date)];
    }

    /// <summary>
    /// The bank's own word, without reading the bank (confirmation 1): the notices of the last hour on the method's bank for this amount whose reference
    /// holds the bill's code, not used by a Paid yet, newest first. The till asks while the QR code is open and shows "Bank confirmed".
    /// </summary>
    public async Task<IReadOnlyList<BankPaymentLine>> NoticesAsync(Guid pointOfSaleId, Guid paymentMethodId, decimal amount, string reference, CancellationToken ct)
    {
        var (bankId, _, _) = await BankAsync(pointOfSaleId, paymentMethodId, ct).ConfigureAwait(false);
        var since = time.GetUtcNow().AddMinutes(-CheckMinutes);
        var text = reference.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var notices = await db.Set<BankNotice>().AsNoTracking()
            .Where(n => n.Active && n.BankId == bankId && n.BankStatementLineId == null && n.Amount == amount && n.ReceivedOn >= since && n.Reference != null)
            .OrderByDescending(n => n.ReceivedOn).Take(20).ToListAsync(ct).ConfigureAwait(false);
        return [.. notices.Where(n => n.Reference!.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Select(n => new BankPaymentLine(n.ExternalId, n.BookedOn, n.Amount, n.Reference, n.ProviderReference ?? n.Reference, n.PayerName, n.PayerIban, n.Booked, true, n.Kind))];
    }

    /// <summary>Check card (a person's press): reads JCC for this payment's link, then answers the notices as the till's Card paid shows them.</summary>
    public async Task<IReadOnlyList<BankPaymentLine>> CheckCardAsync(Guid pointOfSaleId, Guid paymentMethodId, decimal amount, string reference, CancellationToken ct)
    {
        await BankAsync(pointOfSaleId, paymentMethodId, ct).ConfigureAwait(false);
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("pointOfSaleId", "The point of sale belongs to no company.");
        await links.CheckCardAsync(company, pointOfSaleId, paymentMethodId, amount, reference.Trim(), ct).ConfigureAwait(false);
        return await NoticesAsync(pointOfSaleId, paymentMethodId, amount, reference, ct).ConfigureAwait(false);
    }

    /// <summary>Refunds (part of) a card payment made through a payment link; the receipt is then reversed as usual.</summary>
    public async Task<PaymentLinkView> RefundCardAsync(string payToken, decimal amount, CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("payToken", "Choose one of the company's nodes.");
        return await links.RefundAsync(company, payToken, amount, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Keeps a bank line the cashier chose for a till payment, matched to the posted receipt: on the bank's statement of till payments of that day (made when
    /// needed). Refused when the line is held already or its amount is not the receipt's.
    /// </summary>
    public async Task<Guid> KeepAsync(Guid bankId, BankPaymentLine line, Guid paymentId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(line);
        var connection = await db.Set<BankConnection>().AsNoTracking().FirstOrDefaultAsync(c => c.Active && c.BankId == bankId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("bankLine", "The bank account is not connected.");
        var name = $"Till payments {line.Date:yyyy-MM-dd}";
        var statement = await db.Set<BankStatement>().FirstOrDefaultAsync(s => s.Active && s.BankId == bankId && s.ConnectionId == connection.Id && s.FileName == name, ct)
            .ConfigureAwait(false);
        if (statement is null)
        {
            statement = new BankStatement
            {
                BankId = bankId, ConnectionId = connection.Id, FileName = name, PeriodFrom = line.Date, PeriodTo = line.Date, TenantId = connection.TenantId, OwnerId = connection.OwnerId,
            };
            db.Add(statement);
        }

        var kept = new BankStatementLine
        {
            StatementId = statement.Id, LineDate = line.Date, Amount = line.Amount, Text = Clean(line.Text, 500), Reference = Clean(line.Reference, 200), ExternalId = line.ExternalId,
            CounterpartyName = Clean(line.CounterpartyName, 200), CounterpartyIban = BankConnections.Compact(line.CounterpartyIban), DocumentId = paymentId,
            TenantId = connection.TenantId, OwnerId = connection.OwnerId,
        };
        db.Add(kept);

        // The bank's notice of the same line (when there is one) now points at the line it became.
        if (line.ExternalId is { Length: > 0 } external)
        {
            foreach (var notice in await db.Set<BankNotice>().Where(n => n.Active && n.BankId == bankId && n.ExternalId == external && n.BankStatementLineId == null).ToListAsync(ct)
                .ConfigureAwait(false))
            {
                notice.BankStatementLineId = kept.Id;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return kept.Id;
    }

    /// <summary>Checks a chosen bank line before anything is posted: money in, the amount due, not held for the bank yet.</summary>
    public async Task CheckLineAsync(Guid bankId, BankPaymentLine line, decimal due, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(line);
        // A bank moves cents: the amount due is compared as the QR code shows it, rounded to cents (a bill's total may carry more decimals).
        if (due <= 0 || line.Amount != decimal.Round(due, 2, MidpointRounding.AwayFromZero))
        {
            throw ValidationException.For("bankLine", "The bank line's amount is not the amount to pay.");
        }

        if (line.ExternalId is { Length: > 0 } id)
        {
            var statements = db.Set<BankStatement>().Where(s => s.Active && s.BankId == bankId).Select(s => s.Id);
            if (await db.Set<BankStatementLine>().AsNoTracking().AnyAsync(l => l.Active && l.ExternalId == id && statements.Contains(l.StatementId), ct).ConfigureAwait(false))
            {
                throw ValidationException.For("bankLine", "This bank line already pays something else.");
            }
        }
    }

    /// <summary>The EPC069-12 text, version 002, character set 1 (UTF-8), SEPA credit transfer, the reference as unstructured text.</summary>
    public static string Payload(string? bic, string holder, string iban, decimal amount, string reference) =>
        string.Join('\n', "BCD", "002", "1", "SCT", bic ?? string.Empty, holder, iban, Euro + amount.ToString("0.00", CultureInfo.InvariantCulture), string.Empty, string.Empty, reference);

    /// <summary>The QR code as SVG, error correction M as EPC069-12 asks, the text in UTF-8.</summary>
    public static string Svg(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M, forceUtf8: true);
        return new SvgQRCode(data).GetGraphic(6);
    }

    /// <summary>The bank the method's link at the point of sale names, its name, and its identity row.</summary>
    public async Task<(Guid BankId, string? Bank, BankConnection? Connection)> BankAsync(Guid pointOfSaleId, Guid paymentMethodId, CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        if (!nodes.Contains(pointOfSaleId))
        {
            throw ValidationException.For("pointOfSaleId", "There is no such point of sale.");
        }

        var bankId = await db.Set<InterPaymentMethodCostCenter>().AsNoTracking().Where(l => l.Active && l.CostCenterId == pointOfSaleId && l.PaymentMethodId == paymentMethodId && l.CashBankId != null)
            .Select(l => l.CashBankId).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("paymentMethodId", $"{ErrorCodes.NotConfiguredError}: link this payment method to a bank account at this point of sale (Currencies and payments › Payment methods by cost centre, Cash bank).");
        var bank = await db.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == bankId, ct).ConfigureAwait(false);
        var connection = await db.Set<BankConnection>().AsNoTracking().FirstOrDefaultAsync(c => c.Active && c.BankId == bankId, ct).ConfigureAwait(false);
        return (bankId, bank?.Name.In(Models.Base.TranslatedText.DefaultLanguage) ?? bank?.Code, connection);
    }

    private async Task EuroAsync(CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var code = await db.Set<Currency>().AsNoTracking().Where(c => c.Active && c.IsBase && c.OwnerId == company).Select(c => c.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (!string.Equals(code, Euro, StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationException.For("currency", "A payment QR code is in euro only; the company's currency is " + (code ?? "not set") + ".");
        }
    }

    private async Task<string?> CompanyNameAsync(CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == company, ct).ConfigureAwait(false);
        return row?.Name.In(Models.Base.TranslatedText.DefaultLanguage);
    }

    /// <summary>One line of text, without line ends (they would break the EPC text), cut to a length; null when empty.</summary>
    private static string? Clean(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var one = new StringBuilder();
        foreach (var c in value.Trim())
        {
            one.Append(char.IsControl(c) ? ' ' : c);
        }

        var text = one.ToString();
        return text.Length > length ? text[..length] : text;
    }
}

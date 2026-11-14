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
public sealed record BankQrDto(Guid BankId, string? Bank, string Iban, string? Bic, string Holder, decimal Amount, string Currency, string Reference, string Payload, string Svg,
    bool CanCheck);

/// <summary>What Check bank looks for: the method's bank at the point of sale, the amount and the bill's code.</summary>
public sealed record BankCheckRequest(Guid PointOfSaleId, Guid PaymentMethodId, decimal Amount, string Reference);

/// <summary>Paying bills by a bank line the cashier chose after Check bank: the till's usual payment, and that line.</summary>
public sealed record PayByBankRequest(Gamma.Erp.Services.Pos.PayBillsRequest Pay, BankPaymentLine BankLine);

/// <summary>A money-in line from the bank that may be the payment: the newest first, those that mention the reference before the others.</summary>
public sealed record BankPaymentLine(string? ExternalId, DateOnly Date, decimal Amount, string? Text, string? Reference, string? CounterpartyName, string? CounterpartyIban,
    bool Booked, bool MentionsReference);

/// <summary>
/// "Pay by bank" at the till (the founder, 2026-11-14): a payment method marked <see cref="PaymentMethod.ShowsBankQr"/> shows an EPC QR code (EPC069-12,
/// version 002, euro only) for the bank its link at the point of sale names. The payer scans it with their banking app; the cashier then presses
/// Check bank (one read of the bank, started by them) and Paid on the right line, which posts the receipt and matches that bank line in the same press.
/// Without a connection the cashier checks by eye and presses Received; the bank line is then matched later on Bank statements.
/// </summary>
public sealed class PosBankPayments(ErpDbContext db, Visibility visibility, BankConnections connections, TimeProvider? clock = null)
{
    /// <summary>How far back Check bank looks.</summary>
    public const int CheckMinutes = 60;

    private const string Euro = "EUR";

    private readonly TimeProvider time = clock ?? TimeProvider.System;

    /// <summary>The QR code for paying an amount; null when the method shows none.</summary>
    public async Task<BankQrDto?> QrAsync(Guid pointOfSaleId, Guid paymentMethodId, decimal amount, string reference, CancellationToken ct)
    {
        var method = await db.Set<PaymentMethod>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == paymentMethodId && m.Active, ct).ConfigureAwait(false);
        if (method is not { ShowsBankQr: true })
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
        var payload = Payload(connection.Bic, holder, iban, amount, text);
        return new BankQrDto(bankId, bank, iban, connection.Bic, holder, amount, Euro, text, payload, Svg(payload), connection.SessionId is not null && connection.AccountUid is not null);
    }

    /// <summary>
    /// Reads the bank once, for the cashier: the money-in lines of this amount from the last hour (booked or still pending) that no statement line holds
    /// yet, those mentioning the reference first.
    /// </summary>
    public async Task<IReadOnlyList<BankPaymentLine>> CheckAsync(Guid pointOfSaleId, Guid paymentMethodId, decimal amount, string reference, PsuPresence psu, CancellationToken ct)
    {
        var (bankId, _, _) = await BankAsync(pointOfSaleId, paymentMethodId, ct).ConfigureAwait(false);
        var ready = await connections.ReadyAsync(bankId, ct).ConfigureAwait(false);
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
    /// Keeps a bank line the cashier chose for a till payment, matched to the posted receipt: on the bank's statement of till payments of that day (made when
    /// needed). Refused when the line is held already or its amount is not the receipt's.
    /// </summary>
    public async Task KeepAsync(Guid bankId, BankPaymentLine line, Guid paymentId, decimal paid, CancellationToken ct)
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

        db.Add(new BankStatementLine
        {
            StatementId = statement.Id, LineDate = line.Date, Amount = paid, Text = Clean(line.Text, 500), Reference = Clean(line.Reference, 200), ExternalId = line.ExternalId,
            CounterpartyName = Clean(line.CounterpartyName, 200), CounterpartyIban = BankConnections.Compact(line.CounterpartyIban), DocumentId = paymentId,
            TenantId = connection.TenantId, OwnerId = connection.OwnerId,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Checks a chosen bank line before anything is posted: money in, the amount due, not held for the bank yet.</summary>
    public async Task CheckLineAsync(Guid bankId, BankPaymentLine line, decimal due, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Amount != due || due <= 0)
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

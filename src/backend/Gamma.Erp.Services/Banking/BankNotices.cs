using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Banking;

/// <summary>How a bank's message was taken: kept (new or already held), or refused with the reason and the HTTP status to answer.</summary>
public sealed record BankMessageResult(bool Kept, int Status, string Message);

/// <summary>
/// The bank's signed "payment received" message (the founder, 2026-11-16: confirmation 1 of the till's chain). The bank POSTs a JSON body to the
/// company's address <c>/api/bank-responses/{providerId}</c> with
/// <list type="bullet">
/// <item><c>X-Bank-Timestamp</c>: Unix seconds when it was signed;</item>
/// <item><c>X-Bank-Signature</c>: <c>sha256=</c> + hex HMAC-SHA256, with the provider's response secret, of <c>timestamp + "." + body</c>.</item>
/// </list>
/// Nobody is signed in, so every check is the message's own: the signature, its age, the account being one of that company's connected accounts, and its
/// currency. It only writes a <see cref="BankNotice"/>; nothing is posted (nothing runs by itself: the cashier presses Paid).
/// </summary>
public sealed class BankNotices(ErpDbContext db, ISecretBox secrets, TimeProvider? clock = null)
{
    /// <summary>How old (or how far ahead) a signed message may be.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    /// <summary>The largest body taken.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public async Task<BankMessageResult> ReceiveAsync(Guid providerId, string? timestamp, string? signature, string body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var provider = await db.Set<BankProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.Id == providerId, ct).ConfigureAwait(false);
        if (provider?.ResponseSecret is not { Length: > 0 } sealedSecret || provider.OwnerId is not { } company)
        {
            return new(false, 404, "No such address.");
        }

        var secret = secrets.Open(sealedSecret);
        if (secret is null)
        {
            return new(false, 503, "The company's response secret can no longer be opened: an administrator enters it again.");
        }

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || (time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(seconds, 0, 253402300799))).Duration() > MaxAge)
        {
            return new(false, 401, "The message is not signed now (X-Bank-Timestamp).");
        }

        if (!SignatureMatches(secret, timestamp!, body, signature))
        {
            return new(false, 401, "The signature is not the company's (X-Bank-Signature).");
        }

        Message message;
        try
        {
            message = JsonSerializer.Deserialize<Message>(body, Json) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return new(false, 400, "The body is not a payment message.");
        }

        if (message.Type != "payment.received")
        {
            return new(false, 422, $"Messages of type {message.Type} are not taken.");
        }

        var iban = BankConnections.Compact(message.Iban);
        var externalId = message.EntryReference?.Trim();
        if (iban is null || string.IsNullOrEmpty(externalId) || externalId.Length > 200
            || !decimal.TryParse(message.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0
            || string.IsNullOrWhiteSpace(message.Currency) || !DateOnly.TryParseExact(message.BookedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var booked))
        {
            return new(false, 422, "The message needs iban, entryReference, a positive amount, currency and bookedOn.");
        }

        // The account is one of the company's, connected through this provider.
        var connection = await db.Set<BankConnection>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Active && c.OwnerId == company && c.ProviderId == provider.Id && c.Iban == iban, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return new(false, 422, "The account is not one of the company's connected accounts.");
        }

        var currency = message.Currency.Trim().ToUpperInvariant();
        if (connection.Currency is { Length: > 0 } held && !string.Equals(held, currency, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, 422, $"The account is in {held}, the message in {currency}.");
        }

        // The same line, or the same payment the ERP completed itself from a payment link (the bank's reference).
        var bankReference = Cut(message.BankReference, 100);
        if (await db.Set<BankNotice>().AsNoTracking().AnyAsync(n => n.Active && n.ProviderId == provider.Id
                && (n.ExternalId == externalId || (bankReference != null && n.ProviderReference == bankReference)), ct).ConfigureAwait(false))
        {
            return new(true, 200, "Already held.");
        }

        db.Add(new BankNotice
        {
            TenantId = provider.TenantId, OwnerId = company, Kind = "bank", ProviderId = provider.Id, BankId = connection.BankId, ExternalId = externalId,
            MessageId = Cut(message.ResponseId, 100), Amount = amount, Currency = currency, Reference = Cut(message.Reference, 200),
            ProviderReference = Cut(message.BankReference, 100), PayerName = Cut(message.PayerName, 200), PayerIban = BankConnections.Compact(message.PayerIban),
            Booked = !string.Equals(message.Status, "PDNG", StringComparison.OrdinalIgnoreCase), BookedOn = booked, ReceivedOn = time.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Two retries at once: the unique index kept the first.
            return new(true, 200, "Already held.");
        }

        return new(true, 200, "Kept.");
    }

    /// <summary>Compares the signature in constant time.</summary>
    public static bool SignatureMatches(string secret, string timestamp, string body, string? signature)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (signature is null || !signature.StartsWith("sha256=", StringComparison.Ordinal))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(timestamp + "." + body));
        byte[] given;
        try
        {
            given = Convert.FromHexString(signature.AsSpan(7));
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    private static string? Cut(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var v && v.Length > length ? v[..length] : value.Trim();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Message(string? Type, string? ResponseId, string? EntryReference, string? Iban, string? Amount, string? Currency, string? Reference,
        string? BankReference, string? PayerName, string? PayerIban, string? Status, string? BookedOn);
}

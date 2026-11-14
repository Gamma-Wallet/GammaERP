using Gamma.Erp.Models.Payments;

namespace Gamma.Erp.Services.Banking;

/// <summary>A bank a provider reaches.</summary>
public sealed record BankAspsp(string Name, string Country, IReadOnlyList<string> PsuTypes);

/// <summary>An account the bank gave when the person signed in.</summary>
public sealed record BankAccountInfo(string Uid, string? Iban, string? Bic, string? Currency, string? Name);

/// <summary>A session the bank opened: its id, until when, and the accounts it covers.</summary>
public sealed record BankSessionInfo(string SessionId, DateTimeOffset? ValidUntil, IReadOnlyList<BankAccountInfo> Accounts);

/// <summary>The account's booked balance on a day.</summary>
public sealed record BankBalanceInfo(decimal Amount, string? Currency, DateOnly? Date);

/// <summary>
/// One booked line from the bank: its date, money in positive and out negative, the bank's own id, its texts, and the other party.
/// </summary>
public sealed record BankTransactionInfo(DateOnly Date, decimal Amount, string? Currency, string? ExternalId, string? Text, string? Reference, string? CounterpartyName,
    string? CounterpartyIban, bool Booked);

/// <summary>One page of lines and the key of the next one (null on the last).</summary>
public sealed record BankTransactionsPage(IReadOnlyList<BankTransactionInfo> Lines, string? Next);

/// <summary>
/// The person in front of the screen when a read is started, so the bank counts it as theirs, not as a background read (banks cap those at about four a
/// day). Every read in the ERP is started by a person.
/// </summary>
public sealed record PsuPresence(string? IpAddress, string? UserAgent);

/// <summary>
/// An adapter to one kind of open banking provider. It is given the company's provider row and the opened key on every call and keeps nothing.
/// </summary>
public interface IBankConnector
{
    /// <summary>The provider kind it serves (<see cref="BankProvider.Kind"/>).</summary>
    string Kind { get; }

    Task<IReadOnlyList<BankAspsp>> BanksAsync(BankProvider provider, string privateKey, string country, CancellationToken ct);

    /// <summary>Starts a sign-in at the bank; the answer is the address the person opens.</summary>
    Task<string> StartAsync(BankProvider provider, string privateKey, BankAspsp bank, string psuType, string state, DateTimeOffset validUntil, CancellationToken ct);

    /// <summary>Turns the code the bank sent back into a session.</summary>
    Task<BankSessionInfo> SessionAsync(BankProvider provider, string privateKey, string code, CancellationToken ct);

    Task<BankBalanceInfo?> BalanceAsync(BankProvider provider, string privateKey, string accountUid, PsuPresence psu, CancellationToken ct);

    Task<BankTransactionsPage> TransactionsAsync(BankProvider provider, string privateKey, string accountUid, DateOnly fromDay, DateOnly toDay, string? continuation, PsuPresence psu,
        CancellationToken ct);

    /// <summary>Ends a session at the provider; an unknown session is not an error.</summary>
    Task EndAsync(BankProvider provider, string privateKey, string sessionId, CancellationToken ct);
}

/// <summary>The provider refused or could not be reached; the message is the provider's, for the person to read.</summary>
public sealed class BankProviderException(string message) : Exception(message);

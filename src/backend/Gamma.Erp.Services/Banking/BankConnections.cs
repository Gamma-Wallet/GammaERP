using System.Numerics;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Banking;

/// <summary>A bank account cost centre with its identity and connection.</summary>
public sealed record BankConnectionDto(Guid BankId, string? Bank, string? Iban, string? Bic, string? HolderName, string? Currency, bool Connected, string? AspspName,
    string? AspspCountry, DateTimeOffset? ValidUntil, bool Renew, DateOnly? LastFetchedTo, bool Pending, IReadOnlyList<BankAccountInfo> Choices);

/// <summary>An account's identity typed by hand: enough for a payment QR code without a connection.</summary>
public sealed record BankAccountInput(string? Iban, string? Bic, string? HolderName);

/// <summary>Start connecting a bank account: the bank as the provider names it, its country, personal or business.</summary>
public sealed record BankConnectStart(string AspspName, string Country, string PsuType = "business");

/// <summary>What the bank sent back to the return address.</summary>
public sealed record BankConnectFinish(string State, string Code);

/// <summary>Where the person signs in at the bank.</summary>
public sealed record BankConnectLink(string Url);

/// <summary>
/// A company's bank accounts and their connections (the founder, 2026-11-14). A person connects an account: picks the bank, signs in there, and comes back to
/// the ERP's return address, which finishes it. Nothing reads the bank by itself: fetching statements and checking payments are started by a person.
/// </summary>
public sealed class BankConnections(ErpDbContext db, RequestContext request, Visibility visibility, BankStatements statements, ISecretBox secrets,
    IEnumerable<IBankConnector> connectors, TimeProvider? clock = null)
{
    /// <summary>A connection shows Reconnect this many days before it ends.</summary>
    public const int RenewDays = 14;

    private readonly TimeProvider time = clock ?? TimeProvider.System;

    /// <summary>The bank accounts the request sees, each with its identity and connection.</summary>
    public async Task<IReadOnlyList<BankConnectionDto>> ListAsync(string language, CancellationToken ct)
    {
        var banks = await statements.BanksAsync(language, ct).ConfigureAwait(false);
        var ids = banks.Select(b => b.Id).ToList();
        var rows = await db.Set<BankConnection>().AsNoTracking().Where(c => c.Active && ids.Contains(c.BankId)).ToDictionaryAsync(c => c.BankId, ct).ConfigureAwait(false);
        return [.. banks.Select(b => ToDto(b.Id, b.Name, rows.GetValueOrDefault(b.Id)))];
    }

    /// <summary>Sets the account's IBAN, BIC and holder by hand. A connected account keeps the IBAN its bank gave.</summary>
    public async Task<BankConnectionDto> SetAccountAsync(Guid bankId, BankAccountInput input, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (bank, row) = await RowAsync(bankId, language, ct).ConfigureAwait(false);
        var errors = new Dictionary<string, string>();
        var iban = Compact(input.Iban);
        if (iban is not null && !IbanValid(iban))
        {
            errors["iban"] = "This is not a valid IBAN.";
        }
        else if (row?.SessionId is not null && iban != row.Iban)
        {
            errors["iban"] = "The account is connected: its IBAN is the one its bank gave. Disconnect it first to change it.";
        }
        else if (iban is not null && await IbanTakenAsync(iban, bankId, ct).ConfigureAwait(false))
        {
            errors["iban"] = "Another bank account of the company has this IBAN.";
        }

        var bic = Compact(input.Bic);
        if (bic is not null && (bic.Length is not (8 or 11) || !bic.All(char.IsAsciiLetterOrDigit)))
        {
            errors["bic"] = "A BIC has 8 or 11 letters and digits.";
        }

        var holder = string.IsNullOrWhiteSpace(input.HolderName) ? null : input.HolderName.Trim();
        if (holder is { Length: > 70 })
        {
            errors["holderName"] = "The holder's name has at most 70 characters (what a payer's bank shows).";
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        row ??= await NewRowAsync(bankId, ct).ConfigureAwait(false);
        (row.Iban, row.Bic, row.HolderName) = (iban, bic, holder);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(bankId, bank, row);
    }

    /// <summary>The banks the company's provider reaches in a country.</summary>
    public async Task<IReadOnlyList<BankAspsp>> AspspsAsync(string country, CancellationToken ct)
    {
        var (provider, key, connector) = await ProviderAsync(ct).ConfigureAwait(false);
        return await Call(() => connector.BanksAsync(provider, key, Country(country), ct)).ConfigureAwait(false);
    }

    /// <summary>Starts connecting a bank account; the answer is where the person signs in at the bank, which then sends them to the return address.</summary>
    public async Task<BankConnectLink> StartAsync(Guid bankId, BankConnectStart start, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(start);
        var (_, row) = await RowAsync(bankId, language, ct).ConfigureAwait(false);
        var (provider, key, connector) = await ProviderAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(start.AspspName))
        {
            throw ValidationException.For("aspspName", "Choose the bank.");
        }

        var psu = start.PsuType?.Trim().ToLowerInvariant() is "personal" ? "personal" : "business";
        var state = Guid.NewGuid().ToString("N");
        var bank = new BankAspsp(start.AspspName.Trim(), Country(start.Country), [psu]);
        var url = await Call(() => connector.StartAsync(provider, key, bank, psu, state, time.GetUtcNow().AddDays(provider.AccessDays), ct)).ConfigureAwait(false);
        row ??= await NewRowAsync(bankId, ct).ConfigureAwait(false);
        (row.PendingState, row.PendingAccounts, row.AspspName, row.AspspCountry, row.PsuType) = (state, null, bank.Name, bank.Country, psu);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new BankConnectLink(url);
    }

    /// <summary>
    /// Finishes a connection with what the bank sent back. The account is the one with the IBAN already typed, or the only one the bank gave; with several
    /// and no IBAN typed, the person chooses one (<see cref="PickAsync"/>).
    /// </summary>
    public async Task<BankConnectionDto> FinishAsync(BankConnectFinish finish, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(finish);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var row = await db.Set<BankConnection>().FirstOrDefaultAsync(c => c.Active && c.OwnerId == company && c.PendingState == finish.State, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("state", "This bank sign-in was not started here, or it is finished already. Start it again from Bank statements.");
        var (bank, _) = await RowAsync(row.BankId, language, ct).ConfigureAwait(false);
        var (provider, key, connector) = await ProviderAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(finish.Code))
        {
            throw ValidationException.For("code", "The bank sent no code back: the sign-in was cancelled or refused. Start it again.");
        }

        var session = await Call(() => connector.SessionAsync(provider, key, finish.Code.Trim(), ct)).ConfigureAwait(false);
        var accounts = session.Accounts;
        var chosen = row.Iban is { } typed ? accounts.Where(a => Compact(a.Iban) == typed).ToList() : accounts.ToList();
        if (chosen.Count == 0)
        {
            await connector.EndAsync(provider, key, session.SessionId, ct).ConfigureAwait(false);
            row.PendingState = null;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            throw ValidationException.For("iban", row.Iban is null ? "The bank gave no account." : $"The bank gave no account with the IBAN {row.Iban}: sign in with the bank and user that hold it.");
        }

        row.PendingState = null;
        row.ProviderId = provider.Id;
        row.SessionId = secrets.Seal(session.SessionId);
        row.ValidUntil = session.ValidUntil;
        if (chosen.Count == 1)
        {
            await BindAsync(row, chosen[0], ct).ConfigureAwait(false);
        }
        else
        {
            row.AccountUid = null;
            row.PendingAccounts = new JsonArray([.. chosen.Select(a => (JsonNode)new JsonObject
            {
                ["uid"] = a.Uid, ["iban"] = a.Iban, ["bic"] = a.Bic, ["currency"] = a.Currency, ["name"] = a.Name,
            })]);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(row.BankId, bank, row);
    }

    /// <summary>Chooses the account when the bank gave several.</summary>
    public async Task<BankConnectionDto> PickAsync(Guid bankId, string accountUid, string language, CancellationToken ct)
    {
        var (bank, row) = await RowAsync(bankId, language, ct).ConfigureAwait(false);
        var choice = (row is null ? null : Choices(row).FirstOrDefault(a => a.Uid == accountUid))
            ?? throw ValidationException.For("accountUid", "This is not one of the accounts the bank gave.");
        await BindAsync(row!, choice, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(bankId, bank, row);
    }

    /// <summary>Ends the connection at the provider and here; the IBAN, BIC and holder stay (a payment QR code still works).</summary>
    public async Task<BankConnectionDto> DisconnectAsync(Guid bankId, string language, CancellationToken ct)
    {
        var (bank, row) = await RowAsync(bankId, language, ct).ConfigureAwait(false);
        if (row?.SessionId is null && row?.PendingState is null)
        {
            throw ValidationException.For("bankId", "The account is not connected.");
        }

        if (row.SessionId is { } sealedSession && secrets.Open(sealedSession) is { } session
            && await db.Set<BankProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == row.ProviderId && p.Active, ct).ConfigureAwait(false) is { } provider
            && secrets.Open(provider.PrivateKey) is { } key && Connector(provider.Kind) is { } connector)
        {
            await connector.EndAsync(provider, key, session, ct).ConfigureAwait(false);
        }

        (row.ProviderId, row.SessionId, row.AccountUid, row.ValidUntil, row.PendingState, row.PendingAccounts) = (null, null, null, null, null, null);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(bankId, bank, row);
    }

    /// <summary>
    /// What a read of the bank needs: the connection, the provider, the opened key and session, and the adapter. Refused when the account is not connected,
    /// its access ended, or the server can no longer open the key.
    /// </summary>
    public async Task<ReadyConnection> ReadyAsync(Guid bankId, CancellationToken ct)
    {
        var (_, row) = await RowAsync(bankId, "en", ct).ConfigureAwait(false);
        if (row?.SessionId is null || row.AccountUid is null)
        {
            throw ValidationException.For("bankId", $"{ErrorCodes.NotConfiguredError}: the account is not connected to its bank. Connect it on Bank statements.");
        }

        if (row.ValidUntil is { } until && until <= time.GetUtcNow())
        {
            throw ValidationException.For("bankId", "The bank's access ended: connect the account again.");
        }

        var (provider, key, connector) = await ProviderAsync(ct).ConfigureAwait(false);
        if (row.ProviderId != provider.Id)
        {
            throw ValidationException.For("bankId", "The account was connected through a provider the company no longer uses: connect it again.");
        }

        var session = secrets.Open(row.SessionId) ?? throw ValidationException.For("bankId", "The server can no longer open the bank session: connect the account again.");
        return new ReadyConnection(row, provider, key, session, connector);
    }

    /// <summary>A connection ready to read the bank.</summary>
    public sealed record ReadyConnection(BankConnection Connection, BankProvider Provider, string Key, string Session, IBankConnector Connector);

    /// <summary>Runs a provider call; a provider's refusal comes back as a refusal the person reads.</summary>
    public static async Task<T> Call<T>(Func<Task<T>> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (BankProviderException e)
        {
            throw ValidationException.For("bank", e.Message);
        }
    }

    /// <summary>An IBAN written without spaces, in capitals; null when empty.</summary>
    public static string? Compact(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new string([.. value.Where(c => !char.IsWhiteSpace(c))]).ToUpperInvariant();

    /// <summary>The ISO 13616 check: country, check digits, then mod 97 of the rearranged number is 1.</summary>
    public static bool IbanValid(string iban)
    {
        ArgumentNullException.ThrowIfNull(iban);
        if (iban.Length is < 15 or > 34 || !iban.All(char.IsAsciiLetterOrDigit) || !char.IsAsciiLetter(iban[0]) || !char.IsAsciiLetter(iban[1])
            || !char.IsAsciiDigit(iban[2]) || !char.IsAsciiDigit(iban[3]))
        {
            return false;
        }

        var digits = string.Concat((iban[4..] + iban[..4]).Select(c => char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return BigInteger.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) % 97 == 1;
    }

    private async Task BindAsync(BankConnection row, BankAccountInfo account, CancellationToken ct)
    {
        var iban = Compact(account.Iban);
        if (iban is not null && await IbanTakenAsync(iban, row.BankId, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("iban", "Another bank account of the company has this IBAN.");
        }

        row.AccountUid = account.Uid;
        row.Iban = iban ?? row.Iban;
        row.Bic ??= Compact(account.Bic);
        row.HolderName ??= account.Name;
        row.Currency = account.Currency ?? row.Currency;
        row.PendingAccounts = null;
        (row.ConnectedBy, row.ConnectedOn) = (request.UserId, time.GetUtcNow());
    }

    private async Task<bool> IbanTakenAsync(string iban, Guid bankId, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        return await db.Set<BankConnection>().AsNoTracking().AnyAsync(c => c.Active && c.OwnerId == company && c.BankId != bankId && c.Iban == iban, ct).ConfigureAwait(false);
    }

    private BankConnectionDto ToDto(Guid bankId, string? bank, BankConnection? row)
    {
        var connected = row?.SessionId is not null && row.AccountUid is not null;
        var renew = connected && row!.ValidUntil is { } until && until <= time.GetUtcNow().AddDays(RenewDays);
        return new BankConnectionDto(bankId, bank, row?.Iban, row?.Bic, row?.HolderName, row?.Currency, connected, row?.AspspName, row?.AspspCountry, row?.ValidUntil, renew,
            row?.LastFetchedTo, row?.PendingState is not null, row is null ? [] : Choices(row));
    }

    private static List<BankAccountInfo> Choices(BankConnection row) =>
        [.. (row.PendingAccounts?.AsArray() ?? []).OfType<JsonObject>().Select(a => new BankAccountInfo((string?)a["uid"] ?? string.Empty, (string?)a["iban"], (string?)a["bic"],
            (string?)a["currency"], (string?)a["name"]))];

    /// <summary>The bank cost centre the request sees and its connection row, if it has one.</summary>
    private async Task<(string? Bank, BankConnection? Row)> RowAsync(Guid bankId, string language, CancellationToken ct)
    {
        var bank = (await statements.BanksAsync(language, ct).ConfigureAwait(false)).FirstOrDefault(b => b.Id == bankId)
            ?? throw ValidationException.For("bankId", "The bank does not exist.");
        var row = await db.Set<BankConnection>().FirstOrDefaultAsync(c => c.Active && c.BankId == bankId, ct).ConfigureAwait(false);
        return (bank.Name, row);
    }

    private async Task<BankConnection> NewRowAsync(Guid bankId, CancellationToken ct)
    {
        var row = new BankConnection { BankId = bankId, TenantId = request.TenantId, OwnerId = await CompanyAsync(ct).ConfigureAwait(false) };
        db.Add(row);
        return row;
    }

    private async Task<(BankProvider Provider, string Key, IBankConnector Connector)> ProviderAsync(CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var provider = await db.Set<BankProvider>().AsNoTracking().FirstOrDefaultAsync(p => p.Active && p.OwnerId == company, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("provider", $"{ErrorCodes.NotConfiguredError}: the company has no open banking provider. An administrator sets it under Currencies and payments › Open banking.");
        var key = secrets.Open(provider.PrivateKey)
            ?? throw ValidationException.For("provider", "The server can no longer open the provider's key: an administrator chooses the .pem file again under Currencies and payments › Open banking.");
        var connector = Connector(provider.Kind) ?? throw ValidationException.For("provider", $"The ERP has no adapter for {provider.Kind}.");
        return (provider, key, connector);
    }

    private IBankConnector? Connector(string kind) => connectors.FirstOrDefault(c => string.Equals(c.Kind, kind, StringComparison.OrdinalIgnoreCase));

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "Bank accounts belong to a company: choose one of its nodes.");

    private static string Country(string? country) =>
        country?.Trim().ToUpperInvariant() is { Length: 2 } code && code.All(char.IsAsciiLetter) ? code : throw ValidationException.For("country", "The country is its two-letter code.");
}

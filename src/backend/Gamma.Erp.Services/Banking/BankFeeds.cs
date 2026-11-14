using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Reports;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Banking;

/// <summary>What a fetch took: the new statement (none when nothing was new), lines added and already held, pending lines left for a later fetch, and the
/// bank's balance next to the ledger's.</summary>
public sealed record BankFetchDto(Guid? Id, DateOnly From, DateOnly To, int Added, int Skipped, int Pending, decimal? BankBalance, DateOnly? BankBalanceDate,
    decimal LedgerBalance);

/// <summary>
/// Fetching a bank account's statement from its bank (the founder, 2026-11-14): started by a person, never by itself. The lines become a statement like an
/// uploaded one and are decided the same way, by hand. Only booked lines are taken; a line whose bank id is already held for the bank is skipped, and a line
/// without one is skipped by the upload's rule (same date, amount, text and reference). The bank's balance is kept with the statement and shown next to
/// the ledger's; a difference refuses nothing.
/// </summary>
public sealed class BankFeeds(ErpDbContext db, RequestContext request, BankConnections connections, ReportService reports, TimeProvider? clock = null)
{
    /// <summary>How far back a fetch reaches when the account was never fetched.</summary>
    public const int FirstDays = 30;

    /// <summary>The longest period one fetch covers.</summary>
    public const int MaxDays = 730;

    private const int MaxPages = 200;

    private readonly TimeProvider time = clock ?? TimeProvider.System;

    /// <summary>Fetches the account's booked lines from <paramref name="from"/> (default: the last day fetched, or 30 days back) to <paramref name="to"/> (default today).</summary>
    public async Task<BankFetchDto> FetchAsync(Guid bankId, DateOnly? from, DateOnly? to, PsuPresence psu, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(psu);
        var ready = await connections.ReadyAsync(bankId, ct).ConfigureAwait(false);
        var connection = ready.Connection;
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var end = to ?? today;
        var start = from ?? connection.LastFetchedTo ?? end.AddDays(-FirstDays);
        if (start > end || end > today || end.DayNumber - start.DayNumber > MaxDays)
        {
            throw ValidationException.For("from", $"The period runs from a day to a later one, not after today, at most {MaxDays} days.");
        }

        // Every page, booked lines in the period only (some banks ignore the period asked for).
        var fetched = new List<BankTransactionInfo>();
        string? next = null;
        var pages = 0;
        do
        {
            var page = await BankConnections.Call(() => ready.Connector.TransactionsAsync(ready.Provider, ready.Key, connection.AccountUid!, start, end, next, psu, ct)).ConfigureAwait(false);
            fetched.AddRange(page.Lines);
            next = page.Next;
        }
        while (next is not null && ++pages < MaxPages);

        var inPeriod = fetched.Where(l => l.Date >= start && l.Date <= end && l.Amount != 0).ToList();
        var pending = inPeriod.Count(l => !l.Booked);
        var booked = inPeriod.Where(l => l.Booked).ToList();
        var balance = await BankConnections.Call(() => ready.Connector.BalanceAsync(ready.Provider, ready.Key, connection.AccountUid!, psu, ct)).ConfigureAwait(false);

        var statements = db.Set<BankStatement>().Where(s => s.Active && s.BankId == bankId).Select(s => s.Id);
        var held = await db.Set<BankStatementLine>().AsNoTracking().Where(l => l.Active && statements.Contains(l.StatementId))
            .Select(l => new { l.ExternalId, l.LineDate, l.Amount, l.Text, l.Reference }).ToListAsync(ct).ConfigureAwait(false);
        var heldIds = held.Where(l => l.ExternalId != null).Select(l => l.ExternalId!).ToHashSet(StringComparer.Ordinal);
        var heldKeys = held.Where(l => l.ExternalId == null).GroupBy(l => (l.LineDate, l.Amount, l.Text, l.Reference)).ToDictionary(g => g.Key, g => g.Count());
        var fresh = new List<BankStatementLine>();
        foreach (var line in booked)
        {
            var text = Cut(line.Text, 500);
            var reference = Cut(line.Reference, 200);
            if (line.ExternalId is { Length: > 0 } id)
            {
                if (!heldIds.Add(id))
                {
                    continue;
                }
            }
            else if (heldKeys.TryGetValue((line.Date, line.Amount, text, reference), out var left) && left > 0)
            {
                heldKeys[(line.Date, line.Amount, text, reference)] = left - 1;
                continue;
            }

            fresh.Add(new BankStatementLine
            {
                LineDate = line.Date, Amount = line.Amount, Text = text, Reference = reference, ExternalId = Cut(line.ExternalId, 200),
                CounterpartyName = Cut(line.CounterpartyName, 200), CounterpartyIban = BankConnections.Compact(line.CounterpartyIban),
            });
        }

        var tracked = await db.Set<BankConnection>().FirstAsync(c => c.Id == connection.Id, ct).ConfigureAwait(false);
        tracked.LastFetchedTo = end;
        Guid? made = null;
        if (fresh.Count > 0)
        {
            var statement = new BankStatement
            {
                BankId = bankId, ConnectionId = connection.Id, FileName = $"{connection.AspspName} {start:yyyy-MM-dd} – {end:yyyy-MM-dd}", PeriodFrom = start, PeriodTo = end,
                BankBalance = balance?.Amount, BankBalanceDate = balance?.Date ?? end, TenantId = request.TenantId, OwnerId = connection.OwnerId,
            };
            db.Add(statement);
            foreach (var line in fresh.OrderBy(l => l.LineDate))
            {
                (line.StatementId, line.TenantId, line.OwnerId) = (statement.Id, request.TenantId, connection.OwnerId);
                db.Add(line);
            }

            made = statement.Id;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var ledger = await LedgerBalanceAsync(bankId, balance?.Date ?? end, ct).ConfigureAwait(false);
        return new BankFetchDto(made, start, end, fresh.Count, booked.Count - fresh.Count, pending, balance?.Amount, balance?.Date ?? end, ledger);
    }

    /// <summary>The bank cost centre's balance in the ledger up to a day, in the company's currency.</summary>
    public async Task<decimal> LedgerBalanceAsync(Guid bankId, DateOnly until, CancellationToken ct) =>
        (await reports.CostCenterBalanceAsync(bankId, until, ct).ConfigureAwait(false))?.Sum(b => b.Balance) ?? 0;

    private static string? Cut(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var v && v.Length > length ? v[..length] : value.Trim();
}

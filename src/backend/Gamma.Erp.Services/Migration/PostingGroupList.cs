using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Posting;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Posting;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Migration;

/// <summary>
/// The posting groups: one row per line of a scenario. A file replaces the lines of the scenarios it names, and a scenario that would no longer balance
/// is refused, because every future document would post it. A new scenario can start here on a line kind of the document type, with a scope already in use.
/// </summary>
public sealed class PostingGroupList(ErpDbContext db, RequestContext request, Visibility visibility) : MigrationList
{
    private const string Document = "Document type", Parent = "Document type parent", Line = "Transaction type", Scope = nameof(Scope), Account = nameof(Account), Side = nameof(Side),
        Amount = nameof(Amount), Place = nameof(Place), CostCenterPlace = "Cost centre place";

    private const string DebitSide = "Debit", CreditSide = "Credit";

    private const string PlaceHelp = "1 the from side, 2 the to side, 3 the line's own account, 4 the account of the from side, 5 of the to side, 6 of the line's element.";

    private sealed record Key(string? Document, string? Parent, string? Line, string? Scope);

    private sealed record Wanted(Guid? ChartId, string? ChartName, bool Side, Guid ValueElementsId, string? ValueName, int? Value, int? Place, int? CostCenterPlace);

    private readonly Dictionary<Key, Scenario> _scenarios = [];
    private readonly Dictionary<Guid, List<Wanted>> _wanted = [];
    private Dictionary<string, Chart> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Elements> _amounts = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<Guid, int?> _oldValues = [];
    private Dictionary<(string, string?, string), Guid> _links = [];
    private Dictionary<string, Guid> _scopes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Scenario> _new = [];
    private Guid? _owner;

    public override async Task<int> CountAsync(CancellationToken ct)
    {
        var ids = (await RowsOfAsync(ct).ConfigureAwait(false)).Select(r => r.Scenario.Id).ToList();
        return await db.Set<ScenarioConf>().AsNoTracking().CountAsync(l => l.ScenarioId != null && ids.Contains(l.ScenarioId.Value), ct).ConfigureAwait(false);
    }

    public override async Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct)
    {
        var amounts = await AmountsAsync(ct).ConfigureAwait(false);
        return
        [
            new(Document, true, "The document type's code."), new(Parent, false, "The code of the type it sits under, when two types share a code."), new(Line, true, "The transaction type's code."),
            new(Scope, true, "What the scenario covers, as the export shows it."), new(Account, false, "The account's code; empty when the line takes its account from elsewhere (see Place)."),
            new(Side, true, $"{DebitSide} or {CreditSide}."), new(Amount, true, Allowed(amounts.Keys)), new(Place, false, PlaceHelp), new(CostCenterPlace, false, "1 the from side's cost centre, 2 the to side's, empty for none."),
        ];
    }

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        var scenarios = await RowsOfAsync(ct).ConfigureAwait(false);
        var ids = scenarios.Select(s => s.Scenario.Id).ToList();
        var lines = (await db.Set<ScenarioConf>().AsNoTracking().Where(l => l.ScenarioId != null && ids.Contains(l.ScenarioId.Value)).ToListAsync(ct).ConfigureAwait(false)).ToLookup(l => l.ScenarioId!.Value);
        var charts = lines.SelectMany(g => g).Select(l => l.ChartId).OfType<Guid>().Distinct().ToList();
        var codes = await db.Set<Chart>().AsNoTracking().Where(c => charts.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);
        var amounts = (await AmountsAsync(ct).ConfigureAwait(false)).ToDictionary(a => a.Value.Id, a => a.Key);
        return [.. scenarios.SelectMany(s => lines[s.Scenario.Id].OrderBy(l => l.Side).ThenBy(l => l.Id).Select(l => new Dictionary<string, object?>
        {
            [Document] = s.Key.Document, [Parent] = s.Key.Parent, [Line] = s.Key.Line, [Scope] = s.Key.Scope, [Account] = l.ChartId is { } c ? codes.GetValueOrDefault(c) : null,
            [Side] = l.Side ? CreditSide : DebitSide, [Amount] = l.ValueElementsId is { } v ? amounts.GetValueOrDefault(v) : null, [Place] = l.Place, [CostCenterPlace] = l.CostCenterPlace,
        }))];
    }

    public override async Task PrepareAsync(CancellationToken ct)
    {
        foreach (var (scenario, key) in await RowsOfAsync(ct).ConfigureAwait(false))
        {
            _scenarios[key] = scenario;
        }

        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        _owner = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var documents = await db.Set<DocumentType>().AsNoTracking().Where(t => t.OwnerId != null && nodes.Contains(t.OwnerId.Value)).ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);
        var lineTypes = await db.Set<TransactionType>().AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Code, ct).ConfigureAwait(false);
        var links = await db.Set<InterDocumentTypeTransactionType>().AsNoTracking().Where(l => l.DocumentTypeId != null && l.TransactionTypeId != null).ToListAsync(ct).ConfigureAwait(false);
        _links = links.Where(l => documents.ContainsKey(l.DocumentTypeId!.Value) && lineTypes.GetValueOrDefault(l.TransactionTypeId!.Value) is not null).GroupBy(l =>
        {
            var type = documents[l.DocumentTypeId!.Value];
            return ((type.Code ?? string.Empty).ToUpperInvariant(), type.IdSup is { } up ? documents.GetValueOrDefault(up)?.Code?.ToUpperInvariant() : null, lineTypes[l.TransactionTypeId!.Value]!.ToUpperInvariant());
        }).ToDictionary(g => g.Key, g => g.First().Id);
        var scopeIds = _scenarios.Values.Select(v => v.ElementsId).OfType<Guid>().Distinct().ToList();
        _scopes = new((await db.Set<Elements>().AsNoTracking().Where(e => scopeIds.Contains(e.Id)).ToListAsync(ct).ConfigureAwait(false)).Where(e => e.Name is not null)
            .GroupBy(e => e.Name!.In(TranslatedText.DefaultLanguage) ?? string.Empty).Select(g => KeyValuePair.Create(g.Key, g.First().Id)), StringComparer.OrdinalIgnoreCase);
        _accounts = new((await db.Set<Chart>().AsNoTracking().Where(c => c.Code != null && c.OwnerId != null && nodes.Contains(c.OwnerId.Value)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(c => c.Code!, StringComparer.OrdinalIgnoreCase).Select(g => KeyValuePair.Create(g.Key, g.First())), StringComparer.OrdinalIgnoreCase);
        _amounts = await AmountsAsync(ct).ConfigureAwait(false);
        _oldValues = (await db.Set<ScenarioConf>().AsNoTracking().Where(l => l.ValueElementsId != null).Select(l => new { Id = l.ValueElementsId!.Value, l.Value }).Distinct().ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First().Value);
    }

    public override Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var key = new Key(Needed(row, Document), Cell(row, Parent), Needed(row, Line), Needed(row, Scope));
        if (!_scenarios.TryGetValue(key, out var scenario))
        {
            // A new scenario hangs on a line kind of the document type, with a scope one of the company's scenarios already uses.
            var hasLink = _links.TryGetValue((key.Document!.ToUpperInvariant(), key.Parent?.ToUpperInvariant(), key.Line!.ToUpperInvariant()), out var link);
            var hasScope = _scopes.TryGetValue(key.Scope!, out var scope);
            if (!hasLink || !hasScope)
            {
                throw ValidationException.For(Scope, "There is no such scenario, and no transaction type and scope to start one on: add it to the document type first.");
            }

            scenario = new Scenario { Id = Guid.CreateVersion7(), Name = TranslatedText.English(key.Scope!), DocumentTypeTransactionTypeId = link, ElementsId = scope, TenantId = request.TenantId, OwnerId = _owner };
            _scenarios[key] = scenario;
            _new.Add(scenario);
        }


        Chart? account = null;
        if (Cell(row, Account) is { } code && !_accounts.TryGetValue(code, out account))
        {
            throw ValidationException.For(Account, $"There is no account {code}.");
        }

        var side = Needed(row, Side) switch
        {
            var s when string.Equals(s, DebitSide, StringComparison.OrdinalIgnoreCase) => false,
            var s when string.Equals(s, CreditSide, StringComparison.OrdinalIgnoreCase) => true,
            _ => throw ValidationException.For(Side, $"Takes {DebitSide} or {CreditSide}."),
        };
        var amount = _amounts.GetValueOrDefault(Needed(row, Amount)) ?? throw ValidationException.For(Amount, $"Takes {Allowed(_amounts.Keys)}.");
        var wanted = new Wanted(account?.Id, account?.Name.In(TranslatedText.DefaultLanguage), side, amount.Id, Cell(row, Amount), _oldValues.GetValueOrDefault(amount.Id), Whole(row, Place), Whole(row, CostCenterPlace));
        if (!_wanted.TryGetValue(scenario.Id, out var lines))
        {
            _wanted[scenario.Id] = lines = [];
        }

        lines.Add(wanted);
        return Task.FromResult(false);
    }

    public override async Task FinishAsync(CancellationToken ct)
    {
        var posting = new PostingService(db);
        var names = _scenarios.ToDictionary(s => s.Value.Id, s => s.Key);
        var errors = new Dictionary<string, string>();
        if (_new.Count > 0)
        {
            db.AddRange(_new);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        foreach (var (id, lines) in _wanted)
        {
            var stored = await db.Set<ScenarioConf>().Where(l => l.ScenarioId == id).ToListAsync(ct).ConfigureAwait(false);
            static string Same(int? place, int? cost, Guid? chart, bool side, Guid? value) => $"{place}|{cost}|{chart}|{side}|{value}";
            var now = stored.Select(l => Same(l.Place, l.CostCenterPlace, l.ChartId, l.Side, l.ValueElementsId)).Order(StringComparer.Ordinal);
            var then = lines.Select(l => Same(l.Place, l.CostCenterPlace, l.ChartId, l.Side, l.ValueElementsId)).Order(StringComparer.Ordinal);
            if (now.SequenceEqual(then))
            {
                continue;
            }

            db.RemoveRange(stored);
            db.AddRange(lines.Select(l => new ScenarioConf
            {
                ScenarioId = id, ChartId = l.ChartId, ChartName = l.ChartName, Side = l.Side, SideValue = l.Side ? CreditSide : DebitSide, Value = l.Value, ValueName = l.ValueName, ValueElementsId = l.ValueElementsId,
                Place = l.Place, CostCenterPlace = l.CostCenterPlace, TenantId = request.TenantId, OwnerId = stored.FirstOrDefault()?.OwnerId ?? _owner,
            }));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (await posting.CheckScenarioAsync(id, ct).ConfigureAwait(false) is { } reason)
            {
                var key = names[id];
                errors[$"{key.Document} {key.Line} {key.Scope}"] = $"{key.Document}, {key.Line}, {key.Scope}: {reason}";
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static int? Whole(IReadOnlyDictionary<string, string> row, string header) =>
        Cell(row, header) is null ? null : (int)Number(row, header);

    private async Task<Dictionary<string, Elements>> AmountsAsync(CancellationToken ct)
    {
        var table = db.Model.FindEntityType(typeof(Transaction))!.GetTableName();
        var rows = await db.Set<Elements>().AsNoTracking().Where(e => e.Table == table && e.Record != null).ToListAsync(ct).ConfigureAwait(false);
        return new(rows.Where(r => r.Name is not null).GroupBy(r => r.Name!.In(TranslatedText.DefaultLanguage) ?? string.Empty).Select(g => KeyValuePair.Create(g.Key, g.First())), StringComparer.OrdinalIgnoreCase);
    }

    // The company's scenarios with what names each, read as the file names them.
    private async Task<List<(Scenario Scenario, Key Key)>> RowsOfAsync(CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var rows = await (from s in db.Set<Scenario>().AsNoTracking()
                          where s.OwnerId != null && nodes.Contains(s.OwnerId.Value)
                          join link in db.Set<InterDocumentTypeTransactionType>().AsNoTracking() on s.DocumentTypeTransactionTypeId equals link.Id
                          join document in db.Set<DocumentType>().AsNoTracking() on link.DocumentTypeId equals document.Id
                          join line in db.Set<TransactionType>().AsNoTracking() on link.TransactionTypeId equals line.Id
                          join scope in db.Set<Elements>().AsNoTracking() on s.ElementsId equals scope.Id into scopes
                          from e in scopes.DefaultIfEmpty()
                          orderby document.Code, line.Code
                          select new { Scenario = s, Document = document.Code, ParentId = document.IdSup, Line = line.Code, Scope = e }).ToListAsync(ct).ConfigureAwait(false);
        var parents = rows.Select(r => r.ParentId).OfType<Guid>().Distinct().ToList();
        var codes = await db.Set<DocumentType>().AsNoTracking().Where(t => parents.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Code, ct).ConfigureAwait(false);
        return [.. rows.Select(r => (r.Scenario, new Key(r.Document, r.ParentId is { } p ? codes.GetValueOrDefault(p) : null, r.Line, r.Scope?.Name?.In(TranslatedText.DefaultLanguage) ?? r.Scenario.Name?.In(TranslatedText.DefaultLanguage))))];
    }
}

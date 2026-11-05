using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Posting;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Posting;

/// <summary>A scenario that cannot balance: which one, and why.</summary>
public sealed record UnbalancedScenarioDto(Guid Id, string? Scenario, string? DocumentType, string Reason);

/// <summary>One account entry a posting writes; <paramref name="Country"/> only on another chart than the statutory one.</summary>
public sealed record PostingEntry(Guid ChartId, Guid? CostCenterId, Guid TransactionId, decimal Debit, decimal Credit, string? Country = null);

/// <summary>
/// Posting a document to the accounts through its scenarios. Every rule is configuration in the database; the engine only guarantees integrity:
/// <list type="bullet">
/// <item>Each transaction's scenario is the most specific of its document type × transaction type whose scope (an elements row) fits it:
/// the table and category of the transaction's configuration, its element's type, the element itself; an empty part fits all.</item>
/// <item>Each scenario line (tbl_scenario_conf) says the account (or, at place 3, the transaction's element), debit or credit, where it is
/// written (from side, to side), the cost centre it carries, and its value code. The code is a "Transaction amount" row of tbl_elements
/// naming the column of tbl_transaction it reads.</item>
/// <item>All or nothing: every entry is worked out and checked first. With no scenario (<see cref="ErrorCodes.NoScenarioError"/>), a value
/// that names no amount (<see cref="ErrorCodes.NotConfiguredError"/>) or debit ≠ credit (<see cref="ErrorCodes.UnbalancedError"/>),
/// nothing is written.</item>
/// </list>
/// </summary>
public sealed class PostingService(ErpDbContext db)
{
    // The places a line can be written (tbl_scenario_conf.place).
    private const int FromSide = 1;
    private const int ToSide = 2;
    private const int OnElement = 3;
    private const int FromSideAccount = 4;
    private const int ToSideAccount = 5;
    private const int ElementAccount = 6;

    /// <summary>
    /// The entries posting the document would write, checked: a scenario for every transaction, every value naming an amount, debit = credit.
    /// Refuses with every problem at once. Not balanced: a journal of a sheet, whose debit = credit holds over the sheet.
    /// </summary>
    public async Task<IReadOnlyList<PostingEntry>> EntriesAsync(Document document, IReadOnlyList<Transaction> transactions, CancellationToken ct, bool balanced = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transactions);
        var errors = new Dictionary<string, string>();
        var entries = new List<PostingEntry>();
        var (fromSide, toSide) = await SidesAsync(document.TypeId, ct).ConfigureAwait(false);
        foreach (var (t, i) in transactions.Select((t, i) => (t, i)))
        {
            var at = $"transactions[{i}]";
            var scenario = await ScenarioForAsync(document.TypeId, document.CompanyId, document.OwnerId, t, ct).ConfigureAwait(false);
            if (scenario is null)
            {
                errors[at] = $"{ErrorCodes.NoScenarioError}: no scenario fits the transaction {t.Code} ({t.ElementName}).";
                continue;
            }

            var from = t.FromId ?? document.FromId;
            var to = t.ToId ?? document.ToId;
            var lines = await db.Set<ScenarioConf>().AsNoTracking().Where(l => l.ScenarioId == scenario.Id).ToListAsync(ct).ConfigureAwait(false);
            if (lines.Count == 0)
            {
                // Nothing is dropped silently.
                errors[$"{at}.scenario"] = $"{ErrorCodes.NotConfiguredError}: the transaction's scenario has no lines.";
                continue;
            }

            var amounts = await AmountsAsync(lines, ct).ConfigureAwait(false);
            foreach (var line in lines)
            {
                // Written on its side only when the side has an element.
                if ((line.Place is FromSide or FromSideAccount && from is null) || (line.Place is ToSide or ToSideAccount && to is null))
                {
                    continue;
                }

                var chart = line.Place switch
                {
                    OnElement => t.ElementId,
                    FromSideAccount => await SideAccountAsync(fromSide, from, ct).ConfigureAwait(false),
                    ToSideAccount => await SideAccountAsync(toSide, to, ct).ConfigureAwait(false),
                    ElementAccount => await SideAccountAsync(await ScopeAsync(scenario.ElementsId, ct).ConfigureAwait(false), t.ElementId, ct).ConfigureAwait(false),
                    _ => line.ChartId,
                };
                if (chart is null || line.ValueElementsId is not { } value || !amounts.TryGetValue(value, out var read))
                {
                    errors[$"{at}.scenario"] = $"{ErrorCodes.NotConfiguredError}: a line of the scenario has no account or no value that names an amount of the transaction.";
                    continue;
                }

                var amount = read(t);
                if (amount == 0)
                {
                    continue; // nothing to write (e.g. the debit line of a credit-only journal line)
                }

                var costCenter = line.CostCenterPlace switch { 1 => from, 2 => to, _ => null };
                entries.Add(line.Side ? new PostingEntry(chart.Value, costCenter, t.Id, 0, amount) : new PostingEntry(chart.Value, costCenter, t.Id, amount, 0));
            }
        }

        // A group account (one with active accounts under it, the chart's tree) is never posted to: its balance is its accounts'.
        if (await GroupAccountsAsync(entries, ct).ConfigureAwait(false) is { Count: > 0 } groups)
        {
            errors["entries.chart"] = $"{ErrorCodes.GroupAccountError}: {string.Join(", ", groups)} has accounts under it; post to one of those.";
        }

        var (debit, credit) = (entries.Sum(e => e.Debit), entries.Sum(e => e.Credit));
        if (errors.Count == 0 && balanced && debit != credit)
        {
            errors["entries"] = $"{ErrorCodes.UnbalancedError}: debit {debit} ≠ credit {credit}.";
        }

        if (errors.Count == 0)
        {
            entries.AddRange(await MappedAsync(document.CompanyId, entries, errors, ct).ConfigureAwait(false));
        }

        return errors.Count == 0 ? entries : throw new ValidationException(errors);
    }

    /// <summary>The company setting (a property of the Company type) holding its country, carried by the entries of its other charts.</summary>
    public const string CountryCode = "COUNTRY";

    /// <summary>
    /// The statutory entries again on every other chart the company posts to (the corporate's, IFRS…): each account on the account the
    /// company's mapping names in that chart (tbl_inter_chart_chart, the company's rows), carrying the company's country. A chart is posted
    /// to once the company maps an account to it; then every account posted must be mapped there (<see cref="ErrorCodes.MappingError"/>),
    /// once, and never to a group account. The amounts are the statutory ones at 100 %: the ownership share applies at consolidation.
    /// </summary>
    private async Task<List<PostingEntry>> MappedAsync(Guid company, IReadOnlyList<PostingEntry> entries, Dictionary<string, string> errors, CancellationToken ct)
    {
        var mappings = await (from m in db.Set<InterChartChart>().AsNoTracking()
                              join c in db.Set<InterChartCategory>().AsNoTracking() on m.Chart2Id equals c.ElementId
                              join k in db.Set<ChartCategory>().AsNoTracking() on c.CategoryId equals k.Id
                              where m.OwnerId == company && m.Active && m.Chart1Id != null && c.Active && k.Active && !k.IsStatutory
                              select new { Chart1Id = m.Chart1Id!.Value, Chart2Id = c.ElementId, Chart = k.Code }).ToListAsync(ct).ConfigureAwait(false);
        var mapped = new List<PostingEntry>();
        if (mappings.Count == 0)
        {
            return mapped;
        }

        var country = await CountryAsync(company, ct).ConfigureAwait(false);
        var accounts = entries.Select(e => e.ChartId).Distinct().ToList();
        var codes = await db.Set<Chart>().AsNoTracking().Where(c => accounts.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);
        foreach (var chart in mappings.GroupBy(m => m.Chart))
        {
            var to = chart.GroupBy(m => m.Chart1Id).ToDictionary(g => g.Key, g => g.Select(m => m.Chart2Id).Distinct().ToList());
            var missing = accounts.Where(a => !to.ContainsKey(a)).Select(a => codes.GetValueOrDefault(a)).ToList();
            var twice = accounts.Where(a => to.TryGetValue(a, out var targets) && targets.Count > 1).Select(a => codes.GetValueOrDefault(a)).ToList();
            if (missing.Count > 0 || twice.Count > 0)
            {
                errors[$"entries.{chart.Key}"] = $"{ErrorCodes.MappingError}: in chart {chart.Key}, "
                    + string.Join("; ", new[] { missing.Count > 0 ? $"{string.Join(", ", missing)} has no account" : null, twice.Count > 0 ? $"{string.Join(", ", twice)} has more than one" : null }.OfType<string>())
                    + ".";
                continue;
            }

            mapped.AddRange(entries.Select(e => e with { ChartId = to[e.ChartId][0], Country = country }));
        }

        if (errors.Count == 0 && await GroupAccountsAsync(mapped, ct).ConfigureAwait(false) is { Count: > 0 } groups)
        {
            errors["entries.mapping"] = $"{ErrorCodes.GroupAccountError}: the mapping names {string.Join(", ", groups)}, which has accounts under it; map to one of those.";
        }

        return mapped;
    }

    /// <summary>The codes of the entries' accounts that have active accounts under them.</summary>
    private async Task<List<string?>> GroupAccountsAsync(IEnumerable<PostingEntry> entries, CancellationToken ct)
    {
        var charts = entries.Select(e => (Guid?)e.ChartId).Distinct().ToList();
        var groups = await db.Set<Chart>().AsNoTracking().Where(c => c.Active && charts.Contains(c.IdSup)).Select(c => c.IdSup!.Value).Distinct().ToListAsync(ct).ConfigureAwait(false);
        return groups.Count == 0 ? [] : await db.Set<Chart>().AsNoTracking().Where(c => groups.Contains(c.Id)).Select(c => (string?)c.Code).ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The company's country: its value of the Company type's <see cref="CountryCode"/> property.</summary>
    private async Task<string?> CountryAsync(Guid company, CancellationToken ct)
    {
        var values = await (from v in db.Set<CostCenterInfo>().AsNoTracking()
                            join p in db.Set<CostCenterTypeInfo>().AsNoTracking() on v.TypeInfoId equals p.Id
                            where v.ElementId == company && v.Active && p.Active && p.Code == CountryCode
                            select new { v.Value }).ToListAsync(ct).ConfigureAwait(false);
        return values.FirstOrDefault()?.Value switch
        {
            JsonObject text => (string?)text[TranslatedText.DefaultLanguage],
            JsonValue value => value.GetValue<string>(),
            _ => null,
        };
    }

    /// <summary>Writes the entries with their signed copies, + debit and − credit.</summary>
    public void Write(Document document, IEnumerable<PostingEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var e in entries)
        {
            var value = new ChartValue
            {
                CompanyId = document.CompanyId, ChartId = e.ChartId, CostCenterId = e.CostCenterId, DocumentId = document.Id, TransactionId = e.TransactionId,
                PostingDate = document.PostingDate, Debit = e.Debit, Credit = e.Credit, Country = e.Country, OwnerId = document.OwnerId,
            };
            db.Add(value);
            foreach (var signed in new[] { e.Debit, -e.Credit }.Where(v => v != 0))
            {
                db.Add(new ChartCalcValue
                {
                    CompanyId = document.CompanyId, ChartId = e.ChartId, CostCenterId = e.CostCenterId, ChartValueId = value.Id, DocumentId = document.Id,
                    TransactionId = e.TransactionId, PostingDate = document.PostingDate, Value = signed, Country = e.Country, OwnerId = document.OwnerId,
                });
            }
        }
    }

    /// <summary>
    /// The scenario for a transaction: of its document type × transaction type, the one whose scope fits —
    /// the table and category of the transaction's configuration, its element's type and the element itself, an empty part fitting all —
    /// the most specific first (element, then type, then category). Only the scenarios of the document's company (a document written at one of
    /// its cost centres, a bar or a warehouse, posts with the company's configuration), of its owner, or with none.
    /// </summary>
    public async Task<Scenario?> ScenarioForAsync(Guid documentTypeId, Guid company, Guid? owner, Transaction transaction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var links = await db.Set<InterDocumentTypeTransactionType>().AsNoTracking()
            .Where(l => l.DocumentTypeId == documentTypeId && l.TransactionTypeId == transaction.TypeId).Select(l => (Guid?)l.Id).ToListAsync(ct).ConfigureAwait(false);
        var conf = transaction.TypeConfId is { } c
            ? await (from k in db.Set<TransactionTypeConf>().AsNoTracking() join e in db.Set<Elements>().AsNoTracking() on k.ElementsId equals e.Id where k.Id == c select e)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        var candidates = await (from s in db.Set<Scenario>().AsNoTracking()
                                join e in db.Set<Elements>().AsNoTracking() on s.ElementsId equals e.Id
                                where links.Contains(s.DocumentTypeTransactionTypeId) && (s.OwnerId == null || s.OwnerId == company || s.OwnerId == owner)
                                select new { Scenario = s, Scope = e }).ToListAsync(ct).ConfigureAwait(false);
        // The element's type as its own row says (an account's class), not as the line was sent: a scope on a type cannot be met by a
        // line claiming another one.
        var elementType = conf?.Table is { } table && transaction.ElementId is { } element
            ? await ElementTypeAsync(table, element, ct).ConfigureAwait(false) ?? transaction.ElementTypeId
            : transaction.ElementTypeId;
        // A scope on a category is met by the line type's own category, and by every category the element is in or under (a building, in
        // Buildings under Assets): the element's own category first, then the ones above it, then the line type's.
        var categories = conf?.Table is { } t && transaction.ElementId is { } e2
            ? await ElementCategoriesAsync(t, e2, ct).ConfigureAwait(false)
            : [];
        int Rank(Guid? category) => category is not { } c ? 0 : categories.TryGetValue(c, out var depth) ? 100 - depth : c == conf?.CategoryId ? 1 : -1;
        return candidates
            .Where(x => (string.IsNullOrEmpty(x.Scope.Table) || x.Scope.Table == conf?.Table)
                && Rank(x.Scope.CategoryId) >= 0
                && (x.Scope.TypeId is null || x.Scope.TypeId == elementType)
                && (x.Scope.RecordId is null || x.Scope.RecordId == transaction.ElementId))
            .OrderByDescending(x => x.Scope.RecordId is not null).ThenByDescending(x => x.Scope.TypeId is not null).ThenByDescending(x => Rank(x.Scope.CategoryId))
            .Select(x => x.Scenario).FirstOrDefault();
    }

    /// <summary>
    /// The categories an element of an entity table is in (its links) and those above them, each with how far up it is
    /// (0 its own); found through the model, empty when the table has no category links.
    /// </summary>
    private async Task<Dictionary<Guid, int>> ElementCategoriesAsync(string table, Guid element, CancellationToken ct)
    {
        var entity = db.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == table && typeof(Gamma.Erp.Models.Base.Element).IsAssignableFrom(e.ClrType));
        var link = entity is null ? null : db.Model.GetEntityTypes().FirstOrDefault(e => typeof(Gamma.Erp.Models.Base.InterElementCategory).IsAssignableFrom(e.ClrType)
            && e.ClrType.Namespace == entity.ClrType.Namespace && e.ClrType.Name == $"Inter{entity.ClrType.Name}Category");
        var category = entity is null ? null : db.Model.GetEntityTypes().FirstOrDefault(e => typeof(Gamma.Erp.Models.Base.Category).IsAssignableFrom(e.ClrType)
            && e.ClrType.Namespace == entity.ClrType.Namespace);
        var found = new Dictionary<Guid, int>();
        if (link is null || category is null)
        {
            return found;
        }

        string Q(Microsoft.EntityFrameworkCore.Metadata.IEntityType e) => $"\"{e.GetSchema()}\".\"{e.GetTableName()}\"";
        // Names from the model, never from a request; the element is a parameter.
        var ownSql = "SELECT category_id AS \"Value\" FROM " + Q(link) + " WHERE element_id = {0} AND active";
        var own = await db.Database.SqlQueryRaw<Guid>(ownSql, element).ToListAsync(ct).ConfigureAwait(false);
        var parentSql = "SELECT id, id_sup FROM " + Q(category) + " WHERE active";
        var parents = await db.Database.SqlQueryRaw<CategoryParent>(parentSql).ToListAsync(ct).ConfigureAwait(false);
        var up = parents.ToDictionary(p => p.Id, p => p.IdSup);
        foreach (var start in own)
        {
            var depth = 0;
            for (Guid? at = start; at is { } c && depth < 50; at = up.GetValueOrDefault(c), depth++)
            {
                if (!found.TryGetValue(c, out var known) || known > depth)
                {
                    found[c] = depth;
                }
            }
        }

        return found;
    }

    private sealed record CategoryParent(Guid Id, Guid? IdSup);

    /// <summary>The type of an element of an entity table (its type_id), found through the model; null when the table holds no elements.</summary>
    /// <summary>
    /// Whether a scope (an elements row: a table, a category, a type, a record; an empty part fits all) holds an element: it exists in the
    /// scope's table and is in the category or one under it, of the type, the record itself. The same reading as a scenario's scope.
    /// </summary>
    internal async Task<bool> HoldsAsync(Gamma.Erp.Models.Platform.Elements scope, Guid element, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Table is not { } table)
        {
            return true;
        }

        if (await ElementTypeAsync(table, element, ct).ConfigureAwait(false) is not { } type)
        {
            return false;
        }

        return (scope.TypeId is null || scope.TypeId == type) && (scope.RecordId is null || scope.RecordId == element)
            && (scope.CategoryId is not { } category || (await ElementCategoriesAsync(table, element, ct).ConfigureAwait(false)).ContainsKey(category));
    }

    private async Task<Guid?> ElementTypeAsync(string table, Guid element, CancellationToken ct)
    {
        var entity = db.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == table && typeof(Gamma.Erp.Models.Base.Element).IsAssignableFrom(e.ClrType));
        if (entity is null)
        {
            return null;
        }

        var column = entity.FindProperty(nameof(Gamma.Erp.Models.Base.Element.TypeId))!.GetColumnName();
        // Names from the model, never from a request; the id is a parameter.
        var sql = "SELECT \"" + column + "\" AS \"Value\" FROM \"" + entity.GetSchema() + "\".\"" + table + "\" WHERE id = {0}";
        return await db.Database.SqlQueryRaw<Guid?>(sql, element).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether a scenario's lines balance for any amounts (the founder's rule: posting can only be unbalanced when its configuration is).
    /// Worked out, not guessed: the lines are posted for two sample transactions made by the documents' own line formula
    /// (<see cref="Documents.LineAmounts"/>), and each must have debit = credit. Null when it balances, else why not.
    /// </summary>
    public async Task<string?> CheckScenarioAsync(Guid scenarioId, CancellationToken ct)
    {
        var lines = await db.Set<ScenarioConf>().AsNoTracking().Where(l => l.ScenarioId == scenarioId).ToListAsync(ct).ConfigureAwait(false);
        return Check(lines, await AmountsAsync(lines, ct).ConfigureAwait(false));
    }

    /// <summary>The owner's scenarios that cannot balance, named by their document type and line type's scenario, with the reason.</summary>
    public async Task<IReadOnlyList<UnbalancedScenarioDto>> UnbalancedListAsync(Guid? owner, string language, CancellationToken ct)
    {
        var reasons = await UnbalancedScenariosAsync(owner, ct).ConfigureAwait(false);
        var ids = reasons.Keys.ToList();
        var named = await (from s in db.Set<Scenario>().AsNoTracking() where ids.Contains(s.Id)
                           join l in db.Set<InterDocumentTypeTransactionType>().AsNoTracking() on s.DocumentTypeTransactionTypeId equals l.Id into links
                           from l in links.DefaultIfEmpty()
                           join t in db.Set<DocumentType>().AsNoTracking() on l.DocumentTypeId equals t.Id into types
                           from t in types.DefaultIfEmpty()
                           select new { s.Id, s.Name, Type = t.Name }).ToListAsync(ct).ConfigureAwait(false);
        var list = named.Select(n => new UnbalancedScenarioDto(n.Id, n.Name?.In(language), n.Type?.In(language), reasons[n.Id])).ToList();
        // The depreciation scenarios: each line posts the run's amount, so they balance with as many debit lines as credit lines, each on an account.
        var depreciation = await db.Set<Models.Depreciation.DepreciationScenario>().AsNoTracking().Where(s => s.Active && s.OwnerId == owner).ToListAsync(ct).ConfigureAwait(false);
        var scenarioIds = depreciation.Select(s => (Guid?)s.Id).ToList();
        var confs = (await db.Set<Models.Depreciation.DepreciationScenarioConf>().AsNoTracking().Where(l => l.Active && scenarioIds.Contains(l.DepreciationScenarioId))
            .ToListAsync(ct).ConfigureAwait(false)).ToLookup(l => l.DepreciationScenarioId!.Value);
        var bookIds = depreciation.Select(s => s.DepreciationBookId).OfType<Guid>().Distinct().ToList();
        var books = await db.Set<Models.Depreciation.DepreciationBook>().AsNoTracking().Where(b => bookIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name?.In(language), ct)
            .ConfigureAwait(false);
        foreach (var scenario in depreciation)
        {
            var conf = confs[scenario.Id].ToList();
            var reason = conf.Count == 0 ? $"{ErrorCodes.NotConfiguredError}: the depreciation scenario has no lines."
                : conf.Any(l => l.ChartId is null) ? $"{ErrorCodes.NotConfiguredError}: a line of the depreciation scenario names no account."
                : conf.Count(l => !l.Side) != conf.Count(l => l.Side) ? $"{ErrorCodes.UnbalancedError}: each line posts the run's amount, so it needs as many debit lines as credit lines."
                : null;
            if (reason is not null)
            {
                list.Add(new UnbalancedScenarioDto(scenario.Id, scenario.Name?.In(language),
                    $"Depreciation{(scenario.DepreciationBookId is { } book && books.GetValueOrDefault(book) is { } name ? $": {name}" : "")}", reason));
            }
        }

        return [.. list.OrderBy(n => n.DocumentType).ThenBy(n => n.Scenario)];
    }

    /// <summary>The owner's scenarios that cannot balance, with the reason (for the configuration screens and the guided start).</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> UnbalancedScenariosAsync(Guid? owner, CancellationToken ct)
    {
        var ids = await db.Set<Scenario>().AsNoTracking().Where(s => s.OwnerId == owner && s.Active).Select(s => (Guid?)s.Id).ToListAsync(ct).ConfigureAwait(false);
        var lines = (await db.Set<ScenarioConf>().AsNoTracking().Where(l => ids.Contains(l.ScenarioId)).ToListAsync(ct).ConfigureAwait(false)).ToLookup(l => l.ScenarioId!.Value);
        var amounts = await AmountsAsync([.. lines.SelectMany(g => g)], ct).ConfigureAwait(false);
        var reasons = lines.Select(g => (g.Key, Reason: Check([.. g], amounts))).Where(x => x.Reason is not null).ToDictionary(x => x.Key, x => x.Reason!);
        // A scenario with no lines posts nothing: a document that needs it is refused when posted, so it is listed here too.
        foreach (var empty in ids.OfType<Guid>().Where(id => !lines.Contains(id)))
        {
            reasons[empty] = $"{ErrorCodes.NotConfiguredError}: the scenario has no lines; a document that needs it is refused when posted.";
        }

        return reasons;
    }

    private static string? Check(IReadOnlyList<ScenarioConf> lines, Dictionary<Guid, Func<Transaction, decimal>> amounts)
    {
        if (lines.FirstOrDefault(l => l.ValueElementsId is not { } value || !amounts.ContainsKey(value)) is { } unknown)
        {
            return $"{ErrorCodes.NotConfiguredError}: the line's value ({unknown.ValueName ?? unknown.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture)}) names no amount of the transaction.";
        }

        // Two unrelated samples, so that no two amounts are equal by chance; a balanced journal line has debit = credit.
        foreach (var (price, discount, quantity, rate, own) in new[] { (1000.37m, 13.11m, 3m, 20m, 977.3m), (51.9m, 0.7m, 7m, 6m, 18.25m) })
        {
            var (value, vat) = Documents.LineAmounts.Of(price, discount, quantity, rate);
            var sample = new Transaction
            {
                Price = price, Discount = discount, Quantity = quantity, Value = value, Vat = vat, ValueVat = value + vat, ExchangeRate = 1,
                DiscountLcu = discount, ValueLcu = value, VatLcu = vat, ValueVatLcu = value + vat, Debit = own, Credit = own,
            };
            var debit = lines.Where(l => !l.Side).Sum(l => amounts[l.ValueElementsId!.Value](sample));
            var credit = lines.Where(l => l.Side).Sum(l => amounts[l.ValueElementsId!.Value](sample));
            if (debit != credit)
            {
                return $"{ErrorCodes.UnbalancedError}: the lines do not balance (for a sample line, debit {debit} ≠ credit {credit}).";
            }
        }

        return null;
    }

    /// <summary>What the document type's from and to sides are: their elements rows.</summary>
    private async Task<(Elements? From, Elements? To)> SidesAsync(Guid documentTypeId, CancellationToken ct)
    {
        var from = await (from c in db.Set<DocumentTypeConfFrom>().AsNoTracking() join e in db.Set<Elements>().AsNoTracking() on c.ElementsId equals e.Id
                          where c.DocumentTypeId == documentTypeId select e).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var to = await (from c in db.Set<DocumentTypeConfTo>().AsNoTracking() join e in db.Set<Elements>().AsNoTracking() on c.ElementsId equals e.Id
                        where c.DocumentTypeId == documentTypeId select e).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return (from, to);
    }

    /// <summary>
    /// The account a side's element holds (place 4 / 5), e.g. a cash desk's or a bank's: the value of its property that looks up the chart
    /// (kind lookup, pointing at an elements row of the chart's table). Found by the configuration, not by a name. Null when it has none.
    /// </summary>
    private async Task<Elements?> ScopeAsync(Guid? id, CancellationToken ct) =>
        id is { } scope ? await db.Set<Elements>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == scope, ct).ConfigureAwait(false) : null;

    private async Task<Guid?> SideAccountAsync(Elements? side, Guid? elementId, CancellationToken ct)
    {
        if (side?.Table is not { } table || elementId is not { } id)
        {
            return null;
        }

        var element = db.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == table && typeof(Models.Base.Element).IsAssignableFrom(e.ClrType));
        var info = element is null ? null : db.Model.FindEntityType($"{element.ClrType.FullName}Info");
        var typeInfo = element is null ? null : db.Model.FindEntityType($"{element.ClrType.FullName}TypeInfo");
        if (info is null || typeInfo is null)
        {
            return null;
        }

        string Q(Microsoft.EntityFrameworkCore.Metadata.IEntityType e) => $"\"{e.GetSchema()}\".\"{e.GetTableName()}\"";
        var elements = db.Model.FindEntityType(typeof(Elements))!;
        var chart = db.Model.FindEntityType(typeof(Chart))!.GetTableName();
        // Only table names from the model are spliced in; the element, the kind and the chart's table are parameters.
        var sql = $"SELECT btrim(i.value::text, '\"') AS \"Value\" FROM {Q(info)} i JOIN {Q(typeInfo)} p ON p.id = i.type_info_id JOIN {Q(elements)} e ON e.id = p.elements_id " +
            "WHERE i.element_id = {0} AND i.active AND p.active AND p.kind = {1} AND e.\"table\" = {2} LIMIT 1";
        var values = await db.Database.SqlQueryRaw<string>(sql, id, (int)Models.Base.PropertyKind.Lookup, chart!).ToListAsync(ct).ConfigureAwait(false);
        return values.Count > 0 && Guid.TryParse(values[0], out var account) ? account : null;
    }

    /// <summary>
    /// For the lines' "Transaction amount" rows (tbl_elements, by their link), how to read each amount from a transaction: the column of
    /// tbl_transaction the row names (its record), found in the model. No code knows the columns.
    /// </summary>
    private Task<Dictionary<Guid, Func<Transaction, decimal>>> AmountsAsync(IEnumerable<ScenarioConf> lines, CancellationToken ct) =>
        AmountReadersAsync(lines.Where(l => l.ValueElementsId is not null).Select(l => l.ValueElementsId!.Value), ct);

    /// <summary>For "Transaction amount" rows of tbl_elements, how to read each amount from a transaction (the column the row names).</summary>
    public async Task<Dictionary<Guid, Func<Transaction, decimal>>> AmountReadersAsync(IEnumerable<Guid> amountRows, CancellationToken ct)
    {
        var ids = amountRows.Distinct().ToList();
        var rows = await db.Set<Elements>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(ct).ConfigureAwait(false);
        var entity = db.Model.FindEntityType(typeof(Transaction))!;
        var table = entity.GetTableName();
        var result = new Dictionary<Guid, Func<Transaction, decimal>>();
        foreach (var row in rows.Where(r => string.Equals(r.Table, table, StringComparison.OrdinalIgnoreCase)))
        {
            var property = entity.GetProperties().FirstOrDefault(p => string.Equals(p.GetColumnName(), row.Record, StringComparison.OrdinalIgnoreCase));
            if (property?.PropertyInfo is { } info && (info.PropertyType == typeof(decimal?) || info.PropertyType == typeof(decimal)))
            {
                result[row.Id] = t => (decimal?)info.GetValue(t) ?? 0;
            }
        }

        return result;
    }
}

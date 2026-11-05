using System.Globalization;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Currencies;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Migration;

/// <summary>
/// Opening balances: the file becomes one posted journal sheet, each row one journal on a client, supplier, bank, cash desk, member
/// of staff or account, so the balances and the accounts move as on any journal. One more row on the step's opening account balances the sheet.
/// When every step is in, that account stands at zero: that is what proves the detail against the chart. The line kinds, the document type and the
/// opening account are the step's configuration.
/// </summary>
public sealed class OpeningList(ErpDbContext db, RequestContext request, Visibility visibility, DocumentService documents, CurrencyService currencies, MigrationItem item) : MigrationList
{
    private const string Kind = nameof(Kind), Code = nameof(Code), Debit = nameof(Debit), Credit = nameof(Credit), Currency = nameof(Currency), Rate = nameof(Rate), Date = nameof(Date);

    private sealed record Scope(TransactionType Type, Guid ConfId, string Table);

    private sealed record Found(Guid Id, Guid TypeId, string? Code);

    private sealed record Line(Scope Scope, Found Element, decimal Debit, decimal Credit, Guid? CurrencyId, decimal? Rate);

    private readonly string[] _kinds = (item.Entity ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private readonly List<Line> _lines = [];
    private readonly HashSet<(string, Guid, Guid?)> _seen = [];
    private Dictionary<string, Scope> _scopes = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CurrencyDto> _currencies = [];
    private DateOnly? _day;
    private Guid? _document;

    /// <summary>The sheet the import posted.</summary>
    public override Guid? PostedDocument => _document;

    public override async Task<int> CountAsync(CancellationToken ct)
    {
        var runs = await db.Set<MigrationRun>().AsNoTracking().Where(r => r.MigrationItemId == item.Id).ToListAsync(ct).ConfigureAwait(false);
        if (runs.Count == 0)
        {
            return 0;
        }

        // A file with no rows counts too: a company with no such balances has gone past the step.
        return await LiveSheetAsync(db, item.Id, ct).ConfigureAwait(false) is { } sheet ? Math.Max(1, await db.Set<Document>().AsNoTracking().CountAsync(d => d.IdSup == sheet, ct).ConfigureAwait(false)) : runs.Any(r => r.DocumentId is null) ? 1 : 0;
    }

    public override Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct)
    {
        var columns = new List<MigrationColumn>();
        if (_kinds.Length > 1)
        {
            columns.Add(new(Kind, true, Allowed(_kinds)));
        }

        columns.AddRange(
        [
            new(Code, true, "The code of the client, supplier, bank, cash desk, staff member or account."), new(Debit, false, "The amount on the debit side, in the row's currency."),
            new(Credit, false, "The amount on the credit side, in the row's currency."), new(Currency, false, "The currency's code. Empty: the company's."),
            new(Rate, false, "One unit of the currency in the company's. Empty: the rate of the day, from step Exchange rates."), new(Date, true, "The opening day, yyyy-mm-dd, the same on every row."),
        ]);
        return Task.FromResult<IReadOnlyList<MigrationColumn>>(columns);
    }

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        await LoadScopesAsync(ct).ConfigureAwait(false);
        if (await LiveSheetAsync(db, item.Id, ct).ConfigureAwait(false) is not { } sheet)
        {
            return [];
        }

        var offset = await OffsetAsync(ct).ConfigureAwait(false);
        var rows = await (from t in db.Set<Transaction>().AsNoTracking()
                          join d in db.Set<Document>().AsNoTracking() on t.DocumentId equals d.Id
                          where d.IdSup == sheet && t.ElementId != offset.Id
                          orderby d.Code
                          select new { t.TypeId, t.ElementId, t.Debit, t.Credit, t.ExchangeRate, t.CurrencyId, d.PostingDate }).ToListAsync(ct).ConfigureAwait(false);
        var codes = new Dictionary<Guid, string?>();
        foreach (var group in rows.GroupBy(r => _scopes.Values.FirstOrDefault(s => s.Type.Id == r.TypeId)?.Table))
        {
            if (group.Key is not null)
            {
                var ids = group.Select(r => r.ElementId).OfType<Guid>().Distinct().ToArray();
                var sql = $"SELECT id, code, type_id FROM {Table(group.Key)} WHERE id = ANY({{0}})";
                foreach (var row in await db.Database.SqlQueryRaw<Coded>(sql, [ids]).ToListAsync(ct).ConfigureAwait(false))
                {
                    codes[row.Id] = row.Code;
                }
            }
        }

        var currencyCodes = (await currencies.CurrenciesAsync(TranslatedText, ct).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c);
        return [.. rows.Select(r =>
        {
            var currency = r.CurrencyId is { } id ? currencyCodes.GetValueOrDefault(id) : null;
            var rate = currency is { IsBase: false } ? r.ExchangeRate ?? 1 : 1;
            var row = new Dictionary<string, object?>
            {
                [Code] = r.ElementId is { } e ? codes.GetValueOrDefault(e) : null, [Debit] = (r.Debit ?? 0) / rate, [Credit] = (r.Credit ?? 0) / rate,
                [Currency] = currency?.Code, [Rate] = rate == 1 ? null : rate, [Date] = r.PostingDate,
            };
            if (_kinds.Length > 1)
            {
                row[Kind] = _scopes.FirstOrDefault(s => s.Value.Type.Id == r.TypeId).Key;
            }

            return row;
        })];
    }

    private const string TranslatedText = "en";

    public override async Task PrepareAsync(CancellationToken ct)
    {
        if (await LiveSheetAsync(db, item.Id, ct).ConfigureAwait(false) is { } sheet)
        {
            var code = await db.Set<Document>().AsNoTracking().Where(d => d.Id == sheet).Select(d => d.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            throw ValidationException.For("step", $"This step is already posted in journal sheet {code}. Reverse that sheet first: importing again would count the balances twice.");
        }

        await LoadScopesAsync(ct).ConfigureAwait(false);
        await OffsetAsync(ct).ConfigureAwait(false);
        _currencies = await currencies.CurrenciesAsync(TranslatedText, ct).ConfigureAwait(false);
    }

    public override async Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var kind = _kinds.Length == 1 ? _kinds[0] : Needed(row, Kind);
        var scope = _scopes.GetValueOrDefault(kind) ?? throw ValidationException.For(Kind, $"Takes {Allowed(_kinds)}.");
        var code = Needed(row, Code);
        var element = await ElementAsync(scope.Table, code, ct).ConfigureAwait(false) ?? throw ValidationException.For(Code, $"There is no {code}.");
        if (!DateOnly.TryParseExact(Needed(row, Date), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw ValidationException.For(Date, "is not a date, yyyy-mm-dd.");
        }

        if (_day is { } first && first != day)
        {
            throw ValidationException.For(Date, $"Every row carries the same opening day; the first says {first:yyyy-MM-dd}.");
        }

        var debit = Cell(row, Debit) is null ? 0 : Number(row, Debit);
        var credit = Cell(row, Credit) is null ? 0 : Number(row, Credit);
        if (debit < 0 || credit < 0 || (debit == 0) == (credit == 0))
        {
            throw ValidationException.For(Debit, "A row has one amount above zero, on the debit side or the credit side.");
        }

        var currency = Cell(row, Currency) is { } currencyCode
            ? _currencies.FirstOrDefault(c => string.Equals(c.Code, currencyCode, StringComparison.OrdinalIgnoreCase)) ?? throw ValidationException.For(Currency, $"The company has no currency {currencyCode}.")
            : null;
        decimal? rate = null;
        if (currency is { IsBase: false })
        {
            rate = Cell(row, Rate) is null ? await currencies.RateAsync(currency.Id, day, ct).ConfigureAwait(false) : Number(row, Rate);
            if (rate is not > 0)
            {
                throw ValidationException.For(Rate, $"{currency.Code} has no rate on {day:yyyy-MM-dd}: give one here or import the exchange rates first.");
            }
        }

        if (!_seen.Add((kind.ToUpperInvariant(), element.Id, currency?.Id)))
        {
            throw ValidationException.For(Code, "appears twice in the file in this currency.");
        }

        _day = day;
        _lines.Add(new(scope, element, debit * (rate ?? 1), credit * (rate ?? 1), currency?.Id, rate));
        return true;
    }

    public override async Task FinishAsync(CancellationToken ct)
    {
        if (_lines.Count == 0)
        {
            return;
        }

        var entry = item.DocumentTypeId ?? throw ValidationException.For("step", $"{ErrorCodes.NotConfiguredError}: the step names no document type.");
        var type = await db.Set<DocumentType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == entry, ct).ConfigureAwait(false) ?? throw ValidationException.For("step", $"{ErrorCodes.NotConfiguredError}: there is no such document type.");
        var sheetType = type.IdSup ?? throw ValidationException.For("step", $"{ErrorCodes.NotConfiguredError}: the step's document type does not sit under a sheet.");
        var category = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == sheetType).Select(l => l.CategoryId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? request.NodeId;
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var vat = await db.Set<VatGroup>().AsNoTracking().Where(v => v.Rate == 0 && v.OwnerId != null && nodes.Contains(v.OwnerId.Value)).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("step", $"{ErrorCodes.NotConfiguredError}: the company has no VAT group of 0 %.");
        DocumentDto Journal(Scope scope, Found element, decimal debit, decimal credit, Guid? currency, decimal? rate) => new()
        {
            TypeId = entry, FromId = company, ToId = company,
            Transactions = [new TransactionDto { TypeId = scope.Type.Id, TypeConfId = scope.ConfId, ElementId = element.Id, ElementTypeId = element.TypeId, ElementName = element.Code, Price = debit + credit,
                Debit = debit, Credit = credit, VatGroupId = vat, CurrencyId = currency, ExchangeRate = rate }],
        };

        var journals = _lines.Select(l => Journal(l.Scope, l.Element, l.Debit, l.Credit, l.CurrencyId, l.Rate)).ToList();
        var net = _lines.Sum(l => l.Debit - l.Credit);
        if (net != 0)
        {
            var account = await ChartScopeAsync(ct).ConfigureAwait(false);
            var offset = await OffsetAsync(ct).ConfigureAwait(false);
            journals.Add(Journal(account, new(offset.Id, offset.TypeId, offset.Code), net < 0 ? -net : 0, net > 0 ? net : 0, null, null));
        }

        var header = new DocumentDto { TypeId = sheetType, CategoryIds = [category], DocumentDate = _day, PostingDate = _day, FromId = company, ToId = company };
        var saved = await documents.SaveSheetAsync(new SheetDto(header, journals), ct).ConfigureAwait(false);
        _document = (await documents.PostAsync(saved.Sheet.Id!.Value, ct).ConfigureAwait(false)).Id;
    }

    /// <summary>Where the opening account stands, debit less credit.</summary>
    public override async Task<decimal?> AfterAsync(CancellationToken ct) => await OpeningBalanceAsync(db, await OffsetAsync(ct).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>Where a step's opening account stands.</summary>
    public static async Task<decimal?> OpeningBalanceAsync(ErpDbContext db, Visibility visibility, MigrationItem step, CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var account = await db.Set<Chart>().AsNoTracking().FirstOrDefaultAsync(c => c.Code == step.OffsetAccount && c.OwnerId != null && nodes.Contains(c.OwnerId.Value), ct).ConfigureAwait(false);
        return account is null ? null : await OpeningBalanceAsync(db, account).ConfigureAwait(false);
    }

    public static async Task<decimal> OpeningBalanceAsync(ErpDbContext db, Chart account) =>
        await db.Set<ChartValue>().AsNoTracking().Where(v => v.ChartId == account.Id).SumAsync(v => (v.Debit ?? 0) - (v.Credit ?? 0)).ConfigureAwait(false);

    /// <summary>The document a step's file posted, unless a reversal took it back.</summary>
    public static async Task<Guid?> LiveSheetAsync(ErpDbContext db, Guid step, CancellationToken ct)
    {
        var sheets = await db.Set<MigrationRun>().AsNoTracking().Where(r => r.MigrationItemId == step && r.DocumentId != null).OrderByDescending(r => r.CreatedOn).Select(r => r.DocumentId!.Value).ToListAsync(ct).ConfigureAwait(false);
        foreach (var sheet in sheets)
        {
            if (!await db.Set<Document>().AsNoTracking().AnyAsync(d => d.ReversedDocumentId == sheet, ct).ConfigureAwait(false))
            {
                return sheet;
            }
        }

        return null;
    }

    private async Task LoadScopesAsync(CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var wanted = _kinds.ToList();
        var types = await db.Set<TransactionType>().AsNoTracking().Where(t => t.Code != null && wanted.Contains(t.Code) && (t.OwnerId == null || nodes.Contains(t.OwnerId.Value))).ToListAsync(ct).ConfigureAwait(false);
        foreach (var type in types)
        {
            _scopes[type.Code!] = await ScopeOfAsync(type, ct).ConfigureAwait(false);
        }
    }

    private async Task<Scope> ScopeOfAsync(TransactionType type, CancellationToken ct)
    {
        var conf = await (from c in db.Set<TransactionTypeConf>().AsNoTracking()
                          join e in db.Set<Elements>().AsNoTracking() on c.ElementsId equals e.Id
                          where c.TransactionTypeId == type.Id && e.Table != null
                          select new { c.Id, e.Table }).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("step", $"{ErrorCodes.NotConfiguredError}: the transaction type {type.Code} has no scope.");
        return new(type, conf.Id, conf.Table!);
    }

    // The line kind that holds accounts: its scope is the chart's table.
    private async Task<Scope> ChartScopeAsync(CancellationToken ct)
    {
        var table = db.Model.FindEntityType(typeof(Chart))!.GetTableName();
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var found = await (from c in db.Set<TransactionTypeConf>().AsNoTracking()
                           join e in db.Set<Elements>().AsNoTracking() on c.ElementsId equals e.Id
                           join t in db.Set<TransactionType>().AsNoTracking() on c.TransactionTypeId equals t.Id
                           where e.Table == table && (t.OwnerId == null || nodes.Contains(t.OwnerId.Value))
                           select new { Type = t, c.Id }).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("step", $"{ErrorCodes.NotConfiguredError}: no transaction type holds accounts.");
        return new(found.Type, found.Id, table!);
    }

    private async Task<Chart> OffsetAsync(CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var code = item.OffsetAccount ?? throw ValidationException.For("step", $"{ErrorCodes.NotConfiguredError}: the step names no opening account for this country.");
        return await db.Set<Chart>().AsNoTracking().FirstOrDefaultAsync(c => c.Code == code && c.OwnerId != null && nodes.Contains(c.OwnerId.Value), ct).ConfigureAwait(false)
            ?? throw ValidationException.For("step", $"{ErrorCodes.NotConfiguredError}: the chart has no account {code}.");
    }

    private string Table(string table)
    {
        var entity = db.Model.GetEntityTypes().First(e => e.GetTableName() == table);
        return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
    }

    private async Task<Found?> ElementAsync(string table, string code, CancellationToken ct)
    {
        var nodes = (await visibility.NodesAsync(ct).ConfigureAwait(false)).ToArray();
        var settings = (await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false)).ToArray();
        var sql = $"SELECT id, code, type_id FROM {Table(table)} WHERE active AND lower(code) = lower({{0}}) AND (owner_id IS NULL OR owner_id = ANY({{1}}) OR owner_id = ANY({{2}}))";
        var rows = await db.Database.SqlQueryRaw<Coded>(sql, code, nodes, settings).ToListAsync(ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : new(rows[0].Id, rows[0].TypeId, rows[0].Code);
    }

    private sealed record Coded(Guid Id, string? Code, Guid TypeId);
}

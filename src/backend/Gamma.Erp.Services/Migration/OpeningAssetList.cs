using System.Globalization;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.Depreciation;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Depreciation;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Migration;

/// <summary>
/// Opening fixed assets: each row registers an asset in its book with the depreciation it has had, so runs carry on from the opening day; rows of a book that
/// posts also go into one journal sheet, cost on the asset's account and depreciation so far on its own, against the opening account.
/// </summary>
public sealed class OpeningAssetList(ErpDbContext db, RequestContext request, Visibility visibility, DepreciationService depreciation, OpeningList journal, MigrationItem item) : MigrationList
{
    private const string Product = nameof(Product), Unit = nameof(Unit), Book = nameof(Book), Value = nameof(Value), Depreciated = nameof(Depreciated), Start = "Start date", Months = nameof(Months),
        AssetAccount = "Asset account", DepreciationAccount = "Depreciation account", Date = nameof(Date);

    private sealed record Asset(Guid Product, Guid Unit, DepreciationBook Book, decimal Value, decimal Depreciated, DateOnly Start, int? Months, string? AssetAccount, string? DepreciationAccount);

    private readonly List<Asset> _assets = [];
    private readonly HashSet<(Guid, Guid)> _seen = [];
    private Dictionary<string, Guid> _products = new(StringComparer.OrdinalIgnoreCase), _units = new(StringComparer.OrdinalIgnoreCase);
    private List<DepreciationBook> _books = [];
    private DateOnly? _day;
    private Dictionary<string, Guid> _charts = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<(Guid, Guid)> _held = [];

    public override Guid? PostedDocument => journal.PostedDocument;

    public override async Task<int> CountAsync(CancellationToken ct) => await journal.CountAsync(ct).ConfigureAwait(false);

    public override Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MigrationColumn>>(
    [
        new(Product, true, "The product's code."), new(Unit, true, "The unit's code."), new(Book, true, "The depreciation book's name."), new(Value, true, "What the asset cost, in the company's currency."),
        new(Depreciated, false, "The depreciation it has had up to the opening day."), new(Start, true, "The day its depreciation started, yyyy-mm-dd."), new(Months, false, "The months to depreciate over. Empty: its category's."),
        new(AssetAccount, false, "The account holding its cost; needed for a book that posts."), new(DepreciationAccount, false, "The account holding its depreciation; needed when it has any, for a book that posts."),
        new(Date, true, "The opening day, yyyy-mm-dd, the same on every row."),
    ]);

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var rows = await (from v in db.Set<AssetValue>().AsNoTracking()
                          where v.CompanyId == company
                          join s in db.Set<InterProductSpecificUnit>().AsNoTracking() on v.ProductSpecificUnitId equals s.Id
                          join p in db.Set<Product>().AsNoTracking() on s.ProductId equals p.Id
                          join u in db.Set<Unit>().AsNoTracking() on s.UnitId equals u.Id
                          join b in db.Set<DepreciationBook>().AsNoTracking() on v.BookId equals b.Id
                          orderby p.Code
                          select new { v.Id, Product = p.Code, Unit = u.Code, Book = b.Name, v.InitialValue, v.StartDate, v.Months, v.AssetChartId, v.DepreciationChartId }).ToListAsync(ct).ConfigureAwait(false);
        var ids = rows.Select(r => (Guid?)r.Id).ToList();
        var taken = (await db.Set<AssetDepreciation>().AsNoTracking().Where(d => ids.Contains(d.AssetValueId)).ToListAsync(ct).ConfigureAwait(false)).ToLookup(d => d.AssetValueId!.Value);
        var accounts = rows.SelectMany(r => new[] { r.AssetChartId, r.DepreciationChartId }).OfType<Guid>().Distinct().ToList();
        var codes = await db.Set<Chart>().AsNoTracking().Where(c => accounts.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);
        return [.. rows.Select(r => new Dictionary<string, object?>
        {
            [Product] = r.Product, [Unit] = r.Unit, [Book] = r.Book?.In(TranslatedText), [Value] = r.InitialValue, [Depreciated] = taken[r.Id].Sum(d => d.DepreciationValue ?? 0), [Start] = r.StartDate,
            [Months] = r.Months is { } m ? (int)m : null, [Date] = taken[r.Id].MaxBy(d => d.DepreciationDate)?.DepreciationDate,
            [AssetAccount] = r.AssetChartId is { } a ? codes.GetValueOrDefault(a) : null, [DepreciationAccount] = r.DepreciationChartId is { } c ? codes.GetValueOrDefault(c) : null,
        })];
    }

    public override async Task PrepareAsync(CancellationToken ct)
    {
        await journal.PrepareAsync(ct).ConfigureAwait(false);
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var settings = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        _products = new((await db.Set<Product>().AsNoTracking().Where(p => p.Code != null && (p.OwnerId == null || nodes.Contains(p.OwnerId.Value))).Select(p => new { p.Code, p.Id }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(p => p.Code!, StringComparer.OrdinalIgnoreCase).Select(g => KeyValuePair.Create(g.Key, g.First().Id)), StringComparer.OrdinalIgnoreCase);
        _units = new((await db.Set<Unit>().AsNoTracking().Where(u => u.Code != null && (u.OwnerId == null || settings.Contains(u.OwnerId.Value))).Select(u => new { u.Code, u.Id }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(u => u.Code!, StringComparer.OrdinalIgnoreCase).Select(g => KeyValuePair.Create(g.Key, g.First().Id)), StringComparer.OrdinalIgnoreCase);
        _books = await db.Set<DepreciationBook>().AsNoTracking().Where(b => b.OwnerId == null || settings.Contains(b.OwnerId.Value)).ToListAsync(ct).ConfigureAwait(false);
        _charts = await db.Set<Chart>().AsNoTracking().Where(c => c.Code != null && c.OwnerId != null && settings.Contains(c.OwnerId.Value)).ToDictionaryAsync(c => c.Code!, c => c.Id, StringComparer.OrdinalIgnoreCase, ct).ConfigureAwait(false);
        var reversed = await db.Set<MigrationRun>().AsNoTracking().Where(r => r.MigrationItemId == item.Id && r.DocumentId != null).Select(r => r.DocumentId!.Value).ToListAsync(ct).ConfigureAwait(false);
        _held = [.. await (from v in db.Set<AssetValue>().AsNoTracking()
                           join s in db.Set<InterProductSpecificUnit>().AsNoTracking() on v.ProductSpecificUnitId equals s.Id
                           where v.DocumentId == null || !reversed.Contains(v.DocumentId.Value)
                           select new ValueTuple<Guid, Guid>(s.ProductId!.Value, v.BookId!.Value)).ToListAsync(ct).ConfigureAwait(false)];
    }

    public override Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var product = _products.TryGetValue(Needed(row, Product), out var id) ? id : throw ValidationException.For(Product, $"There is no product {Cell(row, Product)}.");
        var unit = _units.TryGetValue(Needed(row, Unit), out var unitId) ? unitId : throw ValidationException.For(Unit, $"There is no unit {Cell(row, Unit)}.");
        var name = Needed(row, Book);
        var book = _books.FirstOrDefault(b => b.Name?.Values.Values.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)) == true)
            ?? throw ValidationException.For(Book, $"There is no book {name}: {Allowed(_books.Select(b => b.Name?.In(TranslatedText)))}.");
        var value = Number(row, Value);
        var taken = Cell(row, Depreciated) is null ? 0 : Number(row, Depreciated);
        if (value <= 0 || taken < 0 || taken > value)
        {
            throw ValidationException.For(Value, "An asset has a value above zero, and depreciation between zero and that value.");
        }

        var day = Day(row, Date);
        if (_day is { } first && first != day)
        {
            throw ValidationException.For(Date, $"Every row carries the same opening day; the first says {first:yyyy-MM-dd}.");
        }

        if (book.Posts && (Cell(row, AssetAccount) is null || (taken > 0 && Cell(row, DepreciationAccount) is null)))
        {
            throw ValidationException.For(AssetAccount, "A book that posts needs the asset's account, and its depreciation account when it has any.");
        }

        foreach (var (header, code) in new[] { (AssetAccount, Cell(row, AssetAccount)), (DepreciationAccount, Cell(row, DepreciationAccount)) })
        {
            if (code is not null && !_charts.ContainsKey(code))
            {
                throw ValidationException.For(header, $"There is no account {code}.");
            }
        }

        if (_held.Contains((product, book.Id)))
        {
            throw ValidationException.For(Product, "This asset is already registered in this book.");
        }

        if (!_seen.Add((product, book.Id)))
        {
            throw ValidationException.For(Product, "appears twice in the file for this book.");
        }

        _day = day;
        _assets.Add(new(product, unit, book, value, taken, Day(row, Start), Cell(row, Months) is null ? null : (int)Number(row, Months), Cell(row, AssetAccount), Cell(row, DepreciationAccount)));
        return Task.FromResult(true);
    }

    public override async Task FinishAsync(CancellationToken ct)
    {
        if (_assets.Count == 0)
        {
            return;
        }

        await RemoveReversedAsync(ct).ConfigureAwait(false);
        var day = _day!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var posting = _assets.Where(a => a.Book.Posts).ToList();
        var rows = posting.GroupBy(a => a.AssetAccount!, StringComparer.OrdinalIgnoreCase).Select(g => (Code: g.Key, Debit: g.Sum(a => a.Value), Credit: 0m))
            .Concat(posting.Where(a => a.Depreciated > 0).GroupBy(a => a.DepreciationAccount!, StringComparer.OrdinalIgnoreCase).Select(g => (Code: g.Key, Debit: 0m, Credit: g.Sum(a => a.Depreciated))));
        foreach (var (code, debit, credit) in rows)
        {
            await journal.SaveAsync(new Dictionary<string, string> { ["Code"] = code, ["Debit"] = debit == 0 ? string.Empty : debit.ToString(CultureInfo.InvariantCulture), ["Credit"] = credit == 0 ? string.Empty : credit.ToString(CultureInfo.InvariantCulture), ["Date"] = day }, ct).ConfigureAwait(false);
        }

        await journal.FinishAsync(ct).ConfigureAwait(false);
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? request.NodeId;
        foreach (var asset in _assets)
        {
            var value = (await depreciation.RegisterAsync(new AssetRegistration(asset.Product, asset.Unit, asset.Book.Id, asset.Value, asset.Start, asset.Months) { DocumentId = journal.PostedDocument }, ct).ConfigureAwait(false))[0];
            var stored = await db.Set<AssetValue>().FirstAsync(v => v.Id == value, ct).ConfigureAwait(false);
            stored.AssetChartId = asset.AssetAccount is { } cost ? _charts[cost] : null;
            stored.DepreciationChartId = asset.DepreciationAccount is { } worn ? _charts[worn] : null;
            if (asset.Depreciated > 0)
            {
                db.Add(new AssetDepreciation
                {
                    CompanyId = company, AssetValueId = value, DepreciationStartDate = asset.Start, DepreciationDate = _day, DepreciationValue = asset.Depreciated, BookValue = asset.Value - asset.Depreciated,
                    Posted = true, DocumentId = journal.PostedDocument, PostedValue = asset.Depreciated, OwnerId = request.NodeId,
                });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public override Task<decimal?> AfterAsync(CancellationToken ct) => journal.AfterAsync(ct);

    // The assets of a reversed file go with it, unless depreciation has run on them since.
    private async Task RemoveReversedAsync(CancellationToken ct)
    {
        var runs = await db.Set<MigrationRun>().AsNoTracking().Where(r => r.MigrationItemId == item.Id && r.DocumentId != null).Select(r => r.DocumentId!.Value).ToListAsync(ct).ConfigureAwait(false);
        var values = await db.Set<AssetValue>().Where(v => v.DocumentId != null && runs.Contains(v.DocumentId.Value)).ToListAsync(ct).ConfigureAwait(false);
        var ids = values.Select(v => (Guid?)v.Id).ToList();
        var taken = await db.Set<AssetDepreciation>().Where(d => ids.Contains(d.AssetValueId)).ToListAsync(ct).ConfigureAwait(false);
        if (taken.Any(d => d.DocumentId is not { } document || !runs.Contains(document)))
        {
            throw ValidationException.For("step", "Depreciation has run on these assets since: undo those runs first.");
        }

        var specifics = values.Select(v => v.ProductSpecificUnitId).ToList();
        var books = values.Select(v => v.BookId).ToList();
        db.RemoveRange(taken);
        db.RemoveRange(await db.Set<AssetValueUnique>().Where(u => specifics.Contains(u.ProductSpecificUnitId) && books.Contains(u.BookId)).ToListAsync(ct).ConfigureAwait(false));
        db.RemoveRange(values);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private const string TranslatedText = "en";

    private static DateOnly Day(IReadOnlyDictionary<string, string> row, string header) =>
        DateOnly.TryParseExact(Needed(row, header), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : throw ValidationException.For(header, "is not a date, yyyy-mm-dd.");
}

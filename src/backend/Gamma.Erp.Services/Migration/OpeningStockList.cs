using System.Globalization;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Migration;

/// <summary>
/// Opening stock: the file becomes one posted document of the step's type, each row a line into its place at the unit cost given, without VAT. The type's
/// configuration makes the cost layers and posts stock against the opening account. A tracked product's serial numbers come on its row, because a
/// line of it does not post without them.
/// </summary>
public sealed class OpeningStockList(ErpDbContext db, RequestContext request, Visibility visibility, DocumentService documents, MigrationItem item) : MigrationList
{
    private const string Place = nameof(Place), Product = nameof(Product), Unit = nameof(Unit), Quantity = nameof(Quantity), UnitCost = "Unit cost", Serials = "Serial numbers", Lot = nameof(Lot), Expiry = "Lot expiry", Date = nameof(Date);

    private sealed record Line(Guid Place, Product Product, Guid Unit, string? UnitName, decimal Quantity, decimal Cost, IReadOnlyList<TransactionSerialDto> Serials, string? Lot, DateOnly? LotExpiry);

    private readonly List<Line> _lines = [];
    private readonly HashSet<(Guid, Guid, Guid, string?)> _seen = [];
    private Dictionary<string, Guid> _places = new(StringComparer.OrdinalIgnoreCase), _units = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Product> _products = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<Guid> _tracked = [], _lotted = [];
    private DateOnly? _day;
    private Guid? _document;

    public override Guid? PostedDocument => _document;

    public override async Task<int> CountAsync(CancellationToken ct)
    {
        if (await OpeningList.LiveSheetAsync(db, item.Id, ct).ConfigureAwait(false) is { } document)
        {
            return Math.Max(1, await db.Set<Transaction>().AsNoTracking().CountAsync(t => t.DocumentId == document, ct).ConfigureAwait(false));
        }

        return await db.Set<MigrationRun>().AsNoTracking().AnyAsync(r => r.MigrationItemId == item.Id && r.DocumentId == null, ct).ConfigureAwait(false) ? 1 : 0;
    }

    public override Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MigrationColumn>>(
    [
        new(Place, true, "The code of the warehouse or place that holds it."), new(Product, true, "The product's code."), new(Unit, true, "The unit's code."), new(Quantity, true, "How much, above zero."),
        new(UnitCost, true, "The cost of one unit in the company's currency, without VAT."), new(Serials, false, $"A product that tracks them: one per unit, separated by {Separator}."), new(Lot, false, "A product that tracks lots: its lot. Several lots of a product: a row each."), new(Expiry, false, "The lot's expiry date, yyyy-mm-dd, if it has one."), new(Date, true, "The opening day, yyyy-mm-dd, the same on every row."),
    ]);

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        if (await OpeningList.LiveSheetAsync(db, item.Id, ct).ConfigureAwait(false) is not { } document)
        {
            return [];
        }

        var rows = await (from t in db.Set<Transaction>().AsNoTracking()
                          where t.DocumentId == document
                          join p in db.Set<Product>().AsNoTracking() on t.ElementId equals p.Id
                          join c in db.Set<CostCenter>().AsNoTracking() on t.ToId equals c.Id
                          join u in db.Set<Unit>().AsNoTracking() on t.UnitId equals u.Id
                          join d in db.Set<Document>().AsNoTracking() on t.DocumentId equals d.Id
                          orderby c.Code, p.Code
                          select new { t.Id, Place = c.Code, Product = p.Code, Unit = u.Code, t.Quantity, t.Price, t.Lot, t.LotExpiryDate, d.PostingDate }).ToListAsync(ct).ConfigureAwait(false);
        var ids = rows.Select(r => r.Id).ToList();
        var serials = (await db.Set<TransactionSerial>().AsNoTracking().Where(s => ids.Contains(s.TransactionId)).ToListAsync(ct).ConfigureAwait(false)).ToLookup(s => s.TransactionId, s => s.Serial);
        return [.. rows.Select(r => new Dictionary<string, object?>
        {
            [Place] = r.Place, [Product] = r.Product, [Unit] = r.Unit, [Quantity] = r.Quantity, [UnitCost] = r.Price, [Serials] = serials[r.Id].Any() ? string.Join(Separator, serials[r.Id]) : null, [Lot] = r.Lot, [Expiry] = r.LotExpiryDate, [Date] = r.PostingDate,
        })];
    }

    public override async Task PrepareAsync(CancellationToken ct)
    {
        if (await OpeningList.LiveSheetAsync(db, item.Id, ct).ConfigureAwait(false) is { } document)
        {
            var code = await db.Set<Document>().AsNoTracking().Where(d => d.Id == document).Select(d => d.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            throw ValidationException.For("step", $"This step is already posted in document {code}. Reverse it first: importing again would count the stock twice.");
        }

        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var settings = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        Dictionary<string, Guid> Index(IEnumerable<(string? Code, Guid Id)> rows) => new(rows.Where(r => r.Code is not null).GroupBy(r => r.Code!, StringComparer.OrdinalIgnoreCase).Select(g => KeyValuePair.Create(g.Key, g.First().Id)), StringComparer.OrdinalIgnoreCase);
        _places = Index((await db.Set<CostCenter>().AsNoTracking().Where(c => c.OwnerId == null || nodes.Contains(c.OwnerId.Value)).Select(c => new { c.Code, c.Id }).ToListAsync(ct).ConfigureAwait(false)).Select(c => (c.Code, c.Id)));
        _units = Index((await db.Set<Unit>().AsNoTracking().Where(c => c.OwnerId == null || settings.Contains(c.OwnerId.Value)).Select(c => new { c.Code, c.Id }).ToListAsync(ct).ConfigureAwait(false)).Select(c => (c.Code, c.Id)));
        _products = new((await db.Set<Product>().AsNoTracking().Where(p => p.Code != null && (p.OwnerId == null || nodes.Contains(p.OwnerId.Value))).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(p => p.Code!, StringComparer.OrdinalIgnoreCase).Select(g => KeyValuePair.Create(g.Key, g.First())), StringComparer.OrdinalIgnoreCase);
        _tracked = [.. await db.Set<ProductType>().AsNoTracking().Where(t => t.TracksSerial).Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false)];
        _lotted = [.. await db.Set<ProductType>().AsNoTracking().Where(t => t.TracksLot).Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false)];
    }

    public override Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var place = _places.TryGetValue(Needed(row, Place), out var id) ? id : throw ValidationException.For(Place, $"There is no place {Cell(row, Place)}.");
        var product = _products.GetValueOrDefault(Needed(row, Product)) ?? throw ValidationException.For(Product, $"There is no product {Cell(row, Product)}.");
        var unit = _units.TryGetValue(Needed(row, Unit), out var unitId) ? unitId : throw ValidationException.For(Unit, $"There is no unit {Cell(row, Unit)}.");
        var (quantity, cost) = (Number(row, Quantity), Number(row, UnitCost));
        if (quantity <= 0 || cost < 0)
        {
            throw ValidationException.For(Quantity, "A row has a quantity above zero and a cost not below zero.");
        }

        if (!DateOnly.TryParseExact(Needed(row, Date), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw ValidationException.For(Date, "is not a date, yyyy-mm-dd.");
        }

        if (_day is { } first && first != day)
        {
            throw ValidationException.For(Date, $"Every row carries the same opening day; the first says {first:yyyy-MM-dd}.");
        }

        var serials = (Cell(row, Serials)?.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []).Select(s => new TransactionSerialDto(s)).ToList();
        if (_tracked.Contains(product.TypeId) && serials.Count != quantity)
        {
            throw ValidationException.For(Serials, $"This product tracks serial numbers: {quantity:0.###} are needed, {serials.Count} are given.");
        }

        if (!_tracked.Contains(product.TypeId) && serials.Count > 0)
        {
            throw ValidationException.For(Serials, "This product does not track serial numbers.");
        }

        var lot = Cell(row, Lot)?.Trim();
        if (_lotted.Contains(product.TypeId) && string.IsNullOrEmpty(lot))
        {
            throw ValidationException.For(Lot, "This product tracks lots: give its lot.");
        }

        if (!_lotted.Contains(product.TypeId) && !string.IsNullOrEmpty(lot))
        {
            throw ValidationException.For(Lot, "This product does not track lots.");
        }

        DateOnly? expiry = null;
        if (!string.IsNullOrWhiteSpace(Cell(row, Expiry)))
        {
            expiry = DateOnly.TryParseExact(Cell(row, Expiry), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw ValidationException.For(Expiry, "is not a date, yyyy-mm-dd.");
        }

        if (!_seen.Add((place, product.Id, unit, lot)))
        {
            throw ValidationException.For(Product, "appears twice in the file for this place and unit.");
        }

        _day = day;
        _lines.Add(new(place, product, unit, Cell(row, Unit), quantity, cost, serials, lot, expiry));
        return Task.FromResult(true);
    }

    public override async Task FinishAsync(CancellationToken ct)
    {
        if (_lines.Count == 0)
        {
            return;
        }

        var type = item.DocumentTypeId ?? throw ValidationException.For("step", $"{ErrorCodes.NotConfiguredError}: this company has no opening stock document type: its pack came before this step.");
        var lineType = await db.Set<InterDocumentTypeTransactionType>().AsNoTracking().Where(l => l.DocumentTypeId == type).Select(l => l.TransactionTypeId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var conf = await db.Set<TransactionTypeConf>().AsNoTracking().Where(c => c.TransactionTypeId == lineType).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var category = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == type).Select(l => l.CategoryId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var vat = await db.Set<VatGroup>().AsNoTracking().Where(v => v.Rate == 0 && v.OwnerId != null && nodes.Contains(v.OwnerId.Value)).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? request.NodeId;
        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = type, CategoryIds = [category], DocumentDate = _day, PostingDate = _day, ToId = company,
            Transactions = [.. _lines.Select(l => new TransactionDto
            {
                TypeId = lineType!.Value, TypeConfId = conf, ElementId = l.Product.Id, ElementTypeId = l.Product.TypeId, ElementName = l.Product.Name.In(TranslatedText), UnitId = l.Unit, UnitName = l.UnitName,
                Quantity = l.Quantity, Price = l.Cost, VatGroupId = vat, ToId = l.Place, Serials = l.Serials, Lot = l.Lot, LotExpiryDate = l.LotExpiry,
            })],
        }, ct).ConfigureAwait(false);
        _document = (await documents.PostAsync(saved.Id!.Value, ct).ConfigureAwait(false)).Id;
    }

    private const string TranslatedText = "en";

    public override async Task<decimal?> AfterAsync(CancellationToken ct) => await OpeningList.OpeningBalanceAsync(db, visibility, item, ct).ConfigureAwait(false);
}

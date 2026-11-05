using System.Globalization;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Posting;

/// <summary>
/// The cost layers one posting changes, first in, first out (the founder's rule, 2026-10-30), worked out in memory and written by
/// <see cref="Movements"/> with the rest of the posting:
/// <list type="bullet">
/// <item>Layers are per place: a place gives up its oldest layers first; a transfer carries what it took, with its cost and purchase date.</item>
/// <item>Nothing goes out that the company does not hold: a line taking more than all its places hold together is refused
/// (<see cref="ErrorCodes.StockError"/>). Where the place may go below zero, the part it lacks is valued at the oldest cost the company holds
/// elsewhere, a layer below zero that what comes in next settles first, the difference to its real cost going into the product's value.</item>
/// <item>Stock comes in only with a cost: a purchase bill makes the layer, everything else brings layers that exist.</item>
/// </list>
/// </summary>
public sealed class CostLayerBook(ErpDbContext db, Document document)
{
    /// <summary>Cost layer modes of a document side (tbl_document_type_conf_from / _to.cost_layers).</summary>
    public const int None = 0, Take = 1, Make = 2, Receive = 3, PutBack = 4, TakeOff = 5, MakeFromUsed = 6, ReceiveConverted = 7, ShareTaken = 8;

    // Kinds of tbl_product_cost_layer_use.
    private const int Taken = 1, PutBackUse = 2, TakenOff = 3, Settled = 4;

    public sealed class Layer
    {
        public Guid Id { get; init; } = Guid.CreateVersion7();
        public bool IsNew { get; init; }
        public bool Changed { get; set; }
        public Guid Holder { get; init; }
        public DateOnly Date { get; init; }
        public DateTimeOffset Created { get; init; }
        public decimal QuantityIn { get; init; }
        public decimal Left { get; set; }
        public decimal ValueLeft { get; set; }
        public decimal UnitCost { get; init; }
        public Guid? TransactionId { get; init; }
        public Guid? OriginLayerId { get; init; }
    }

    /// <summary>A part of a layer a line took (or brings in): how much, at what value, bought when.</summary>
    public sealed record Piece(decimal Quantity, decimal Value, DateOnly Date, Guid? Origin, Layer? Into = null);

    private readonly Dictionary<(Guid Product, Guid Unit), List<Layer>> _layers = [];
    private readonly List<(Guid Layer, Transaction Line, Guid Product, Guid Unit, decimal Quantity, decimal Value, int Kind)> _uses = [];
    private readonly List<(Guid Product, Guid Unit, decimal Difference)> _settlements = [];

    private DateOnly Today => document.PostingDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>The line's stock out of a place, oldest layers first; returns the pieces taken (their value is the line's cost), or the error.</summary>
    public async Task<(List<Piece> Pieces, string? Error)> TakeAsync(Guid holder, Guid product, Guid unit, decimal quantity, Transaction line, string? name, CancellationToken ct)
    {
        var all = await LayersAsync(product, unit, ct).ConfigureAwait(false);
        var held = all.Sum(l => l.Left);
        if (held < quantity)
        {
            return ([], $"{ErrorCodes.StockError}: the company holds {Show(held)} of {name}, {Show(quantity)} are needed.");
        }

        var pieces = new List<Piece>();
        var need = quantity;
        foreach (var layer in Oldest(all.Where(l => l.Holder == holder && l.Left > 0)))
        {
            if (need == 0)
            {
                break;
            }

            var q = Math.Min(need, layer.Left);
            var v = Part(layer, q);
            (layer.Left, layer.ValueLeft, layer.Changed) = (layer.Left - q, layer.ValueLeft - v, true);
            _uses.Add((layer.Id, line, product, unit, q, v, Taken));
            pieces.Add(new Piece(q, v, layer.Date, layer.OriginLayerId ?? layer.Id));
            need -= q;
        }

        // What the place lacks (it may go below zero) is valued at the oldest cost the company holds elsewhere, and left owing at the place.
        foreach (var other in Oldest(all.Where(l => l.Holder != holder && l.Left > 0)))
        {
            if (need == 0)
            {
                break;
            }

            var q = Math.Min(need, other.Left);
            var v = Part(other, q);
            var owed = new Layer { IsNew = true, Changed = true, Holder = holder, Date = Today, Created = DateTimeOffset.UtcNow, QuantityIn = 0, Left = -q, ValueLeft = -v,
                UnitCost = other.UnitCost, TransactionId = line.Id };
            all.Add(owed);
            _uses.Add((owed.Id, line, product, unit, q, v, Taken));
            pieces.Add(new Piece(q, v, other.Date, other.OriginLayerId ?? other.Id));
            need -= q;
        }

        return (pieces, null);
    }

    /// <summary>Takes off the reversed line's own layer (a reversal of a purchase); refused when part of it has gone.</summary>
    public async Task<(List<Piece> Pieces, string? Error)> TakeOffAsync(Guid holder, Guid product, Guid unit, decimal quantity, Transaction line, string? name, CancellationToken ct)
    {
        var all = await LayersAsync(product, unit, ct).ConfigureAwait(false);
        var own = all.Where(l => l.TransactionId == line.IdSup && l.Holder == holder && l.Left > 0).ToList();
        if (line.IdSup is null || own.Sum(l => l.Left) < quantity)
        {
            return ([], $"{ErrorCodes.StockError}: {Show(own.Sum(l => l.Left))} of {name} from the reversed line are still at the place, {Show(quantity)} are reversed; " +
                "what was sold or moved is reversed first.");
        }

        var pieces = new List<Piece>();
        var need = quantity;
        foreach (var layer in own)
        {
            var q = Math.Min(need, layer.Left);
            var v = Part(layer, q);
            (layer.Left, layer.ValueLeft, layer.Changed) = (layer.Left - q, layer.ValueLeft - v, true);
            _uses.Add((layer.Id, line, product, unit, q, v, TakenOff));
            pieces.Add(new Piece(q, v, layer.Date, layer.Id));
            need -= q;
            if (need == 0)
            {
                break;
            }
        }

        return (pieces, null);
    }

    /// <summary>The layers the reversed line took, put back (latest first; a reversal of a sale); returns the pieces, or the error.</summary>
    public async Task<(List<Piece> Pieces, string? Error)> PutBackAsync(Guid holder, Guid product, Guid unit, decimal quantity, Transaction line, string? name, CancellationToken ct)
    {
        var all = await LayersAsync(product, unit, ct).ConfigureAwait(false);
        var used = line.IdSup is not { } original ? [] : await db.Set<ProductCostLayerUse>().AsNoTracking()
            .Where(u => u.TransactionId == original && (u.Kind == Taken || u.Kind == PutBackUse)).Join(db.Set<InterProductSpecificUnit>().AsNoTracking()
                .Where(s => s.ProductId == product && s.UnitId == unit), u => u.ProductSpecificUnitId, s => s.Id, (u, _) => u)
            .GroupBy(u => u.LayerId).Select(g => new { Layer = g.Key, Quantity = g.Sum(u => u.Quantity), Value = g.Sum(u => u.ValueLcu) }).ToListAsync(ct).ConfigureAwait(false);
        // The layers it took may be used up since: they come back too.
        var missing = used.Select(u => u.Layer).Where(id => all.All(l => l.Id != id)).ToList();
        all.AddRange((await db.Set<ProductCostLayer>().AsNoTracking().Where(l => l.Active && missing.Contains(l.Id)).ToListAsync(ct).ConfigureAwait(false)).Select(Of));
        var layers = all.ToDictionary(l => l.Id);
        var pieces = new List<Piece>();
        var need = quantity;
        foreach (var u in used.Where(u => u.Quantity > 0 && layers.ContainsKey(u.Layer)).OrderByDescending(u => layers[u.Layer].Date).ThenByDescending(u => layers[u.Layer].Created))
        {
            if (need == 0)
            {
                break;
            }

            var q = Math.Min(need, u.Quantity);
            var v = q == u.Quantity ? u.Value : Round(u.Value * q / u.Quantity);
            var layer = layers[u.Layer];
            pieces.Add(new Piece(q, v, layer.Date, layer.OriginLayerId ?? layer.Id, layer.Holder == holder ? layer : null));
            need -= q;
        }

        return need > 0
            ? ([], $"{ErrorCodes.StockError}: the reversed line took {Show(quantity - need)} of {name} from the stock, {Show(quantity)} are reversed.")
            : (await BringAsync(holder, product, unit, pieces, line, PutBackUse, ct).ConfigureAwait(false), null);
    }

    /// <summary>
    /// Stock coming into a place with its cost: first it settles what the place owes (its layers below zero, oldest first, the difference
    /// to the cost they were valued at going into the product's value), the rest becomes its layers (or goes back into the layer it came from).
    /// </summary>
    public async Task<List<Piece>> BringAsync(Guid holder, Guid product, Guid unit, IEnumerable<Piece> pieces, Transaction line, int kind, CancellationToken ct)
    {
        var all = await LayersAsync(product, unit, ct).ConfigureAwait(false);
        var brought = new List<Piece>();
        foreach (var piece in pieces)
        {
            var (q, v) = (piece.Quantity, piece.Value);
            foreach (var owed in Oldest(all.Where(l => l.Holder == holder && l.Left < 0)))
            {
                if (q == 0)
                {
                    break;
                }

                var s = Math.Min(q, -owed.Left);
                var real = s == q ? v : Round(v * s / q);
                var valued = s == -owed.Left ? -owed.ValueLeft : Round(-owed.ValueLeft * s / -owed.Left);
                (owed.Left, owed.ValueLeft, owed.Changed) = (owed.Left + s, owed.ValueLeft + valued, true);
                _uses.Add((owed.Id, line, product, unit, -s, real - valued, Settled));
                if (real != valued)
                {
                    _settlements.Add((product, unit, real - valued));
                }

                (q, v) = (q - s, v - real);
            }

            if (q == 0)
            {
                brought.Add(piece);
                continue;
            }

            if (piece.Into is { } into)
            {
                (into.Left, into.ValueLeft, into.Changed) = (into.Left + q, into.ValueLeft + v, true);
                _uses.Add((into.Id, line, product, unit, -q, -v, kind));
            }
            else
            {
                var layer = new Layer
                {
                    IsNew = true, Changed = true, Holder = holder, Date = piece.Date, Created = DateTimeOffset.UtcNow, QuantityIn = q, Left = q, ValueLeft = v,
                    UnitCost = Math.Round(v / q, 6, MidpointRounding.AwayFromZero), TransactionId = line.Id, OriginLayerId = piece.Origin,
                };
                all.Add(layer);
                if (kind == PutBackUse)
                {
                    _uses.Add((layer.Id, line, product, unit, -q, -v, kind));
                }
            }

            brought.Add(piece);
        }

        return brought;
    }

    /// <summary>A purchase's piece: the quantity at its value, bought on the posting date.</summary>
    public Piece Bought(decimal quantity, decimal value) => new(quantity, value, Today, null);

    /// <summary>Writes the layers, their uses and the settled differences into the product's value; called by <see cref="Movements.ApplyAsync"/>.</summary>
    public async Task ApplyAsync(Func<Guid, Guid, CancellationToken, Task<Guid>> specificOf, CancellationToken ct)
    {
        foreach (var ((product, unit), layers) in _layers)
        {
            var specific = await specificOf(product, unit, ct).ConfigureAwait(false);
            foreach (var layer in layers.Where(l => l.Changed))
            {
                if (layer.IsNew)
                {
                    db.Add(new ProductCostLayer
                    {
                        Id = layer.Id, OwnerId = document.OwnerId, CompanyId = document.CompanyId, HolderId = layer.Holder, ProductSpecificUnitId = specific, DocumentId = document.Id,
                        TransactionId = layer.TransactionId, OriginLayerId = layer.OriginLayerId, LayerDate = layer.Date, QuantityIn = layer.QuantityIn, QuantityLeft = layer.Left,
                        ValueLeftLcu = layer.ValueLeft, UnitCostLcu = layer.UnitCost,
                    });
                    continue;
                }

                var row = db.ChangeTracker.Entries<ProductCostLayer>().Select(e => e.Entity).FirstOrDefault(r => r.Active && r.Id == layer.Id)
                    ?? await db.Set<ProductCostLayer>().FirstAsync(r => r.Id == layer.Id, ct).ConfigureAwait(false);
                (row.QuantityLeft, row.ValueLeftLcu) = (layer.Left, layer.ValueLeft);
            }
        }

        foreach (var (layer, line, product, unit, quantity, value, kind) in _uses)
        {
            db.Add(new ProductCostLayerUse
            {
                OwnerId = document.OwnerId, CompanyId = document.CompanyId, LayerId = layer, DocumentId = document.Id, TransactionId = line.Id,
                ProductSpecificUnitId = await specificOf(product, unit, ct).ConfigureAwait(false), Quantity = quantity, ValueLcu = value, Kind = kind,
            });
        }
    }

    /// <summary>The differences settling owed layers left in the product's value: credit when the real cost was higher, debit when lower.</summary>
    public IEnumerable<(Guid Product, Guid Unit, decimal Difference)> Settlements => _settlements;

    private async Task<List<Layer>> LayersAsync(Guid product, Guid unit, CancellationToken ct)
    {
        if (_layers.TryGetValue((product, unit), out var known))
        {
            return known;
        }

        var rows = await (from l in db.Set<ProductCostLayer>().AsNoTracking()
                          join s in db.Set<InterProductSpecificUnit>().AsNoTracking() on l.ProductSpecificUnitId equals s.Id
                          where l.Active && l.CompanyId == document.CompanyId && s.ProductId == product && s.UnitId == unit && l.QuantityLeft != 0
                          select l).ToListAsync(ct).ConfigureAwait(false);
        var layers = rows.Select(Of).ToList();
        _layers[(product, unit)] = layers;
        return layers;
    }

    private static Layer Of(ProductCostLayer l) => new()
    {
        Id = l.Id, Holder = l.HolderId, Date = l.LayerDate, Created = l.CreatedOn, QuantityIn = l.QuantityIn, Left = l.QuantityLeft, ValueLeft = l.ValueLeftLcu,
        UnitCost = l.UnitCostLcu, TransactionId = l.TransactionId, OriginLayerId = l.OriginLayerId,
    };

    private static List<Layer> Oldest(IEnumerable<Layer> layers) => layers.OrderBy(l => l.Date).ThenBy(l => l.Created).ToList();

    private static decimal Part(Layer layer, decimal quantity) => quantity == layer.Left ? layer.ValueLeft : Round(layer.ValueLeft * quantity / layer.Left);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Show(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

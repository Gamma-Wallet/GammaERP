using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Stock;

/// <summary>
/// Split stock at a place into another unit: how much of which product, from which unit into which, on which day. The unit split document
/// type is the company's only one unless <see cref="TypeId"/> names it.
/// </summary>
public sealed record ConvertStockRequest(Guid CostCenterId, Guid ProductId, Guid FromUnitId, Guid ToUnitId, decimal Quantity, DateOnly? Date = null, string? Note = null,
    Guid? TypeId = null);

/// <summary>
/// Units of measure in use: the factor between two units from the company's conversions (a
/// product's own first, then those for every product, either way round), and splitting stock into the unit it is sold in. A split is a "Unit split" document (the founder's rule, 2026-10-30): the quantity out of
/// the product in the first unit, the converted quantity in, in the second, each cost layer on its own with its unit cost changed by the same
/// factor (1 kg at 10 € becomes 1000 g at 0.010 €), so the value and the books do not change.
/// </summary>
public sealed class UnitConversionService(ErpDbContext db, RequestContext request, Visibility visibility, TimeProvider clock)
{
    /// <summary>How many <paramref name="to"/> one <paramref name="from"/> is, or null when the company has no conversion between them.</summary>
    public async Task<decimal?> FactorAsync(Guid from, Guid to, Guid? product, CancellationToken ct)
    {
        if (from == to)
        {
            return 1;
        }

        // A general conversion only within one tree of units (cm and m, never cm and kg); a product's own may cross them (a pack of 250 g).
        var sameTree = await SameTreeAsync(db, from, to, ct).ConfigureAwait(false);
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var rows = await db.Set<UnitConversion>().AsNoTracking()
            .Where(c => ((c.FromUnitId == from && c.ToUnitId == to) || (c.FromUnitId == to && c.ToUnitId == from))
                        && (c.ProductId == null ? sameTree : c.ProductId == product)
                        && c.Factor > 0 && (c.OwnerId == null || nodes.Contains(c.OwnerId.Value)))
            .ToListAsync(ct).ConfigureAwait(false);
        var best = rows.OrderBy(c => c.ProductId is null).ThenBy(c => c.FromUnitId != from).FirstOrDefault();
        return best is null ? null : best.FromUnitId == from ? best.Factor : 1 / best.Factor;
    }

    /// <summary>
    /// Whether two units are of one tree: the same unit at the top of their parents (the founder, 2026-10-31: m with cm, mm and km under it;
    /// a conversion is only between units of one tree, never cm to kg).
    /// </summary>
    public static async Task<bool> SameTreeAsync(ErpDbContext db, Guid a, Guid b, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var parents = await db.Set<Unit>().AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.IdSup, ct).ConfigureAwait(false);
        Guid Top(Guid unit)
        {
            var seen = new HashSet<Guid> { unit };
            while (parents.GetValueOrDefault(unit) is { } up && parents.ContainsKey(up) && seen.Add(up))
            {
                unit = up;
            }

            return unit;
        }

        return Top(a) == Top(b);
    }

    /// <summary>Makes and posts the unit split document.</summary>
    public async Task<DocumentDto> ConvertAsync(ConvertStockRequest convert, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(convert);
        if (convert.Quantity <= 0)
        {
            throw ValidationException.For("quantity", "The quantity is above 0.");
        }

        if (convert.FromUnitId == convert.ToUnitId)
        {
            throw ValidationException.For("toUnitId", "Choose another unit to split into.");
        }

        var factor = await FactorAsync(convert.FromUnitId, convert.ToUnitId, convert.ProductId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("toUnitId", $"{ErrorCodes.NotConfiguredError}: there is no conversion between these units.");
        return await new StockDocuments(db, request, visibility, clock).PostAsync(StockDocuments.UnitSplit, convert.TypeId, convert.CostCenterId, convert.CostCenterId,
            convert.Date, [new StockDocumentLine(2, convert.ProductId, convert.FromUnitId, convert.Quantity),
                new StockDocumentLine(3, convert.ProductId, convert.ToUnitId, Math.Round(convert.Quantity * factor, 6, MidpointRounding.AwayFromZero))], [], convert.Note, ct)
            .ConfigureAwait(false);
    }
}

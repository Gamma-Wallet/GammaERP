using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Pos;

/// <summary>
/// Where a point of sale is (the founder, 2026-11-04): a place is a point of sale when its kind has settings; a place below one, such as a shop's till,
/// works as that point of sale. A cashier signs in at the till: the bill takes the shop's stock and records the till, and the till has its own shift.
/// </summary>
public static class PosPlaces
{
    /// <summary>The point of sale a place works as: the place itself when its kind has settings, else the nearest place above it whose kind has; null when none.</summary>
    public static async Task<Guid?> PointOfSaleOfAsync(ErpDbContext db, Guid place, CancellationToken ct)
    {
        var kinds = (await db.Set<PointOfSaleSetting>().AsNoTracking().Where(r => r.Active && r.PointOfSaleId == null && r.PointOfSaleTypeId != null)
            .Select(r => r.PointOfSaleTypeId!.Value).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        Guid? at = place;
        for (var depth = 0; at is { } id && depth < 64; depth++)
        {
            var row = await db.Set<CostCenter>().AsNoTracking().Where(c => c.Id == id).Select(c => new { c.TypeId, c.IdSup }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (row is null)
            {
                return null;
            }

            if (kinds.Contains(row.TypeId))
            {
                return id;
            }

            at = row.IdSup;
        }

        return null;
    }

    /// <summary>
    /// The settings rows that apply at a place, first to last: its own row, then (for a till) its point of sale's own row, then that point of sale's kind's row,
    /// then the company's default. Each column is read from the first row that sets it.
    /// </summary>
    public static async Task<(Guid? PointOfSale, List<PointOfSaleSetting> Chain)> ChainAsync(ErpDbContext db, IReadOnlySet<Guid> nodes, Guid? place, CancellationToken ct)
    {
        var pointOfSale = place is { } p ? await PointOfSaleOfAsync(db, p, ct).ConfigureAwait(false) ?? p : (Guid?)null;
        var rows = (await db.Set<PointOfSaleSetting>().AsNoTracking().Where(r => r.Active && (r.PointOfSaleId == null || r.PointOfSaleId == place || r.PointOfSaleId == pointOfSale))
            .ToListAsync(ct).ConfigureAwait(false)).Where(r => r.OwnerId is null || nodes.Contains(r.OwnerId.Value)).ToList();
        var kindId = pointOfSale is { } s ? await db.Set<CostCenter>().AsNoTracking().Where(c => c.Id == s).Select(c => (Guid?)c.TypeId).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
        var chain = new[]
        {
            place is null ? null : rows.FirstOrDefault(r => r.PointOfSaleId == place),
            pointOfSale is null || pointOfSale == place ? null : rows.FirstOrDefault(r => r.PointOfSaleId == pointOfSale),
            kindId is null ? null : rows.FirstOrDefault(r => r.PointOfSaleId == null && r.PointOfSaleTypeId == kindId),
            rows.FirstOrDefault(r => r.PointOfSaleId == null && r.PointOfSaleTypeId == null),
        }.OfType<PointOfSaleSetting>().ToList();
        return (pointOfSale, chain);
    }
}

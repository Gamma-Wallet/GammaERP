using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Employees;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Stock;

/// <summary>A lot and who holds how much of it.</summary>
public sealed record LotRowDto(Guid Id, Guid LotId, Guid ProductId, string? ProductCode, string? Product, string Lot, DateOnly? ExpiryDate, Guid HolderId, string? Holder, Guid UnitId, string? Unit, decimal Quantity);

/// <summary>A lot a holder has, to pick on a line.</summary>
public sealed record HeldLotDto(Guid Id, string Lot, DateOnly? ExpiryDate, decimal Quantity);

/// <summary>A product that tracks lots at a place: how much stock it holds and how much of that has a lot.</summary>
public sealed record LotStockDto(Guid CostCenterId, string? CostCenter, Guid ProductId, string? ProductCode, string? Product, Guid UnitId, string? Unit, decimal Quantity, decimal InLots);

/// <summary>A lot given to stock already held.</summary>
public sealed record AssignLotRequest(Guid CostCenterId, Guid ProductId, Guid UnitId, string Lot, DateOnly? ExpiryDate, decimal Quantity);

/// <summary>
/// The lots: which batch is where and when it expires, always read by hand (nothing alerts), and lots given to stock already held. Stock and cost stay under the product.
/// </summary>
public sealed class LotService(ErpDbContext db, RequestContext request, Visibility visibility)
{
    private const int MaxRows = 500;

    /// <summary>What is held of each lot, soonest expiry first; those matching a search on lot, product or holder, or expiring by a day.</summary>
    public async Task<IReadOnlyList<LotRowDto>> ListAsync(string? search, DateOnly? expiresBy, string language, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var rows = await (from s in db.Set<ProductLotStock>().AsNoTracking()
                          join l in db.Set<ProductLot>().AsNoTracking() on s.ProductLotId equals l.Id
                          where s.Active && l.Active && s.CompanyId == company && s.Quantity > 0 && (expiresBy == null || l.ExpiryDate <= expiresBy)
                          select new { Stock = s, Lot = l }).ToListAsync(ct).ConfigureAwait(false);
        var productIds = rows.Select(r => r.Lot.ProductId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        var holders = await HoldersAsync([.. rows.Select(r => r.Stock.HolderId).Distinct()], language, ct).ConfigureAwait(false);
        var unitIds = rows.Select(r => r.Stock.UnitId).Distinct().ToList();
        var units = await db.Set<Models.Units.Unit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name.In(language), ct).ConfigureAwait(false);
        var term = search?.Trim();
        return [.. rows.Select(r =>
        {
            var product = products.GetValueOrDefault(r.Lot.ProductId);
            return new LotRowDto(r.Stock.Id, r.Lot.Id, r.Lot.ProductId, product?.Code, product?.Name.In(language), r.Lot.Lot, r.Lot.ExpiryDate, r.Stock.HolderId, holders.GetValueOrDefault(r.Stock.HolderId),
                r.Stock.UnitId, units.GetValueOrDefault(r.Stock.UnitId), r.Stock.Quantity);
        }).Where(r => string.IsNullOrEmpty(term) || new[] { r.Lot, r.Product, r.ProductCode, r.Holder }.Any(x => x?.Contains(term, StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(r => r.ExpiryDate ?? DateOnly.MaxValue).ThenBy(r => r.Lot).Take(MaxRows)];
    }

    /// <summary>The lots a holder has of a product, earliest expiry first; nothing is chosen for the person.</summary>
    public async Task<IReadOnlyList<HeldLotDto>> HeldAsync(Guid productId, Guid holderId, Guid? unitId, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var rows = await (from s in db.Set<ProductLotStock>().AsNoTracking()
                          join l in db.Set<ProductLot>().AsNoTracking() on s.ProductLotId equals l.Id
                          where s.Active && l.Active && s.CompanyId == company && l.ProductId == productId && s.HolderId == holderId && s.Quantity > 0 && (unitId == null || s.UnitId == unitId)
                          select new { l.Id, l.Lot, l.ExpiryDate, s.Quantity }).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.GroupBy(r => (r.Id, r.Lot, r.ExpiryDate)).Select(g => new HeldLotDto(g.Key.Id, g.Key.Lot, g.Key.ExpiryDate, g.Sum(r => r.Quantity))).OrderBy(r => r.ExpiryDate ?? DateOnly.MaxValue).ThenBy(r => r.Lot)];
    }

    /// <summary>The products of the request's company that track lots: a point of sale asks for the lot of these.</summary>
    public async Task<IReadOnlyList<Guid>> TrackedAsync(CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        return await (from p in db.Set<Product>().AsNoTracking() join k in db.Set<ProductType>().AsNoTracking() on p.TypeId equals k.Id
                      where p.Active && k.TracksLot && !p.IsComposite && (p.OwnerId == null || nodes.Contains(p.OwnerId.Value)) select p.Id).ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The products that track lots the company's places hold, with how much of that has a lot.</summary>
    public async Task<IReadOnlyList<LotStockDto>> StockAsync(string language, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var stock = await (from s in db.Set<InterCostCenterProductStock>().AsNoTracking()
                           join u in db.Set<InterProductSpecificUnit>().AsNoTracking() on s.ProductSpecificUnitId equals u.Id
                           join p in db.Set<Product>().AsNoTracking() on u.ProductId equals p.Id
                           join k in db.Set<ProductType>().AsNoTracking() on p.TypeId equals k.Id
                           where s.CompanyId == company && k.TracksLot && s.CostCenterId != null && s.Quantity > 0 && u.UnitId != null
                           select new { CostCenterId = s.CostCenterId!.Value, ProductId = p.Id, p.Code, p.Name, UnitId = u.UnitId!.Value, Quantity = s.Quantity ?? 0 }).ToListAsync(ct).ConfigureAwait(false);
        var places = stock.Select(s => s.CostCenterId).Distinct().ToList();
        var inLots = (await (from s in db.Set<ProductLotStock>().AsNoTracking() join l in db.Set<ProductLot>().AsNoTracking() on s.ProductLotId equals l.Id
                             where s.Active && s.CompanyId == company && places.Contains(s.HolderId)
                             group s by new { s.HolderId, l.ProductId, s.UnitId } into g
                             select new { g.Key.HolderId, g.Key.ProductId, g.Key.UnitId, Quantity = g.Sum(x => x.Quantity) }).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(s => (s.HolderId, s.ProductId, s.UnitId), s => s.Quantity);
        var names = await db.Set<CostCenter>().AsNoTracking().Where(c => places.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name.In(language), ct).ConfigureAwait(false);
        var unitIds = stock.Select(s => s.UnitId).Distinct().ToList();
        var units = await db.Set<Models.Units.Unit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name.In(language), ct).ConfigureAwait(false);
        return [.. stock.Select(s => new LotStockDto(s.CostCenterId, names.GetValueOrDefault(s.CostCenterId), s.ProductId, s.Code, s.Name.In(language), s.UnitId, units.GetValueOrDefault(s.UnitId),
            s.Quantity, inLots.GetValueOrDefault((s.CostCenterId, s.ProductId, s.UnitId)))).OrderBy(s => s.CostCenter).ThenBy(s => s.Product)];
    }

    /// <summary>Gives a lot to stock a place already holds, never more than it holds without one. The stock and its cost stay as they are.</summary>
    public async Task AssignAsync(AssignLotRequest assign, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assign);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var tracks = await (from p in db.Set<Product>().AsNoTracking() join k in db.Set<ProductType>().AsNoTracking() on p.TypeId equals k.Id
                            where p.Id == assign.ProductId select (bool?)k.TracksLot).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (tracks != true)
        {
            throw ValidationException.For("productId", "This product does not track lots: set it on its type first.");
        }

        var number = assign.Lot?.Trim();
        if (string.IsNullOrEmpty(number) || assign.Quantity <= 0)
        {
            throw ValidationException.For("lot", "Give a lot and a quantity above zero.");
        }

        var held = await (from s in db.Set<InterCostCenterProductStock>().AsNoTracking()
                          join u in db.Set<InterProductSpecificUnit>().AsNoTracking() on s.ProductSpecificUnitId equals u.Id
                          where s.CompanyId == company && s.CostCenterId == assign.CostCenterId && u.ProductId == assign.ProductId && u.UnitId == assign.UnitId
                          select s.Quantity ?? 0).SumAsync(ct).ConfigureAwait(false);
        var inLots = await (from s in db.Set<ProductLotStock>().AsNoTracking() join l in db.Set<ProductLot>().AsNoTracking() on s.ProductLotId equals l.Id
                            where s.Active && s.CompanyId == company && s.HolderId == assign.CostCenterId && s.UnitId == assign.UnitId && l.ProductId == assign.ProductId
                            select s.Quantity).SumAsync(ct).ConfigureAwait(false);
        if (inLots + assign.Quantity > held)
        {
            throw ValidationException.For("quantity", $"The place holds {held:0.###}, of which {inLots:0.###} have a lot: {assign.Quantity:0.###} more do not fit.");
        }

        var lot = await db.Set<ProductLot>().FirstOrDefaultAsync(l => l.Active && l.CompanyId == company && l.ProductId == assign.ProductId && l.Lot == number, ct).ConfigureAwait(false);
        if (lot is null)
        {
            lot = new ProductLot { CompanyId = company, ProductId = assign.ProductId, Lot = number, ExpiryDate = assign.ExpiryDate, TenantId = request.TenantId, OwnerId = company };
            db.Add(lot);
        }
        else if (assign.ExpiryDate is { } given && lot.ExpiryDate is { } date && given != date)
        {
            throw ValidationException.For("expiryDate", $"Lot {number} expires on {date:yyyy-MM-dd}.");
        }
        else
        {
            lot.ExpiryDate ??= assign.ExpiryDate;
        }

        var row = await db.Set<ProductLotStock>().FirstOrDefaultAsync(s => s.Active && s.ProductLotId == lot.Id && s.UnitId == assign.UnitId && s.HolderId == assign.CostCenterId, ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new ProductLotStock { CompanyId = company, ProductLotId = lot.Id, UnitId = assign.UnitId, HolderId = assign.CostCenterId, HolderTable = db.Model.FindEntityType(typeof(CostCenter))!.GetTableName(),
                TenantId = request.TenantId, OwnerId = company };
            db.Add(row);
        }

        row.Quantity += assign.Quantity;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<Dictionary<Guid, string?>> HoldersAsync(List<Guid> ids, string language, CancellationToken ct)
    {
        var names = await db.Set<CostCenter>().AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name.In(language), ct).ConfigureAwait(false);
        foreach (var (id, name) in await db.Set<Partner>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name.In(language), ct).ConfigureAwait(false))
        {
            names[id] = name;
        }

        foreach (var (id, name) in await db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name.In(language), ct).ConfigureAwait(false))
        {
            names[id] = name;
        }

        return names;
    }

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "Lots belong to a company: choose one of its nodes.");
}

using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Stock;

/// <summary>A unit of a product or a product type, in its list's order; in use when something points at it, so it cannot be removed.</summary>
public sealed record ProductUnitDto(Guid UnitId, string? Code, string? Name, bool InUse);

/// <summary>
/// A product's units (its own, the first being the default a line proposes) and its type's (offered when it has none of its own), and
/// whether the request may change them.
/// </summary>
public sealed record ProductUnitsDto(IReadOnlyList<ProductUnitDto> Units, IReadOnlyList<ProductUnitDto> TypeUnits, bool CanWrite);

/// <summary>
/// The units products and product types are sold, bought and kept in (the founder, 2026-10-31: a product can have more than one, its type's are shown when it has none). A list is saved whole, in order: new
/// units are added, removed ones switched off, unless something points at them (stock, prices, cost layers, assets, a document line).
/// </summary>
public sealed class ProductUnitService(ErpDbContext db, RequestContext request, Visibility visibility)
{
    private const string ProductTable = "tbl_product";

    public async Task<ProductUnitsDto?> ProductAsync(Guid productId, string language, CancellationToken ct)
    {
        if (await VisibleProductAsync(productId, ct).ConfigureAwait(false) is not { } product)
        {
            return null;
        }

        var own = await OwnAsync(productId, ct).ConfigureAwait(false);
        var used = await ProductUnitsInUseAsync(productId, own, ct).ConfigureAwait(false);
        var typeUnits = await TypeRowsAsync(product.TypeId, ct).ConfigureAwait(false);
        var names = await NamesAsync(own.Select(r => r.UnitId!.Value).Concat(typeUnits.Select(r => r.UnitId!.Value)), language, ct).ConfigureAwait(false);
        return new ProductUnitsDto([.. own.Select(r => Dto(r.UnitId!.Value, names, used.Contains(r.UnitId!.Value)))],
            [.. typeUnits.Select(r => Dto(r.UnitId!.Value, names, false))], await MayWriteAsync(productId, ct).ConfigureAwait(false));
    }

    /// <summary>Saves a product's units in the order given; refuses a unit that does not exist and the removal of one in use.</summary>
    public async Task<ProductUnitsDto> SaveProductAsync(Guid productId, IReadOnlyList<Guid> unitIds, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(unitIds);
        var product = await VisibleProductAsync(productId, ct).ConfigureAwait(false) ?? throw ValidationException.For("productId", "There is no such product.");
        if (!await MayWriteAsync(productId, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("rights", "You may not write products of this category.");
        }

        var wanted = await CheckedUnitsAsync(unitIds, ct).ConfigureAwait(false);
        var own = await db.Set<InterProductSpecificUnit>().Where(r => r.ProductId == productId && r.UnitId != null).ToListAsync(ct).ConfigureAwait(false);
        var used = await ProductUnitsInUseAsync(productId, own, ct).ConfigureAwait(false);
        var stays = own.Where(r => !wanted.Contains(r.UnitId!.Value) && used.Contains(r.UnitId!.Value)).Select(r => r.UnitId!.Value).ToList();
        if (stays.Count > 0)
        {
            var names = await NamesAsync(stays, language, ct).ConfigureAwait(false);
            throw ValidationException.For("unitIds", $"In use, so it stays: {string.Join(", ", stays.Select(u => names.GetValueOrDefault(u).Name ?? u.ToString()))}.");
        }

        foreach (var row in own.Where(r => !wanted.Contains(r.UnitId!.Value)))
        {
            row.Active = false;
        }

        foreach (var (unit, i) in wanted.Select((u, i) => (u, i)))
        {
            if (own.FirstOrDefault(r => r.UnitId == unit) is { } row)
            {
                if (row.SortOrder != i)
                {
                    row.SortOrder = i;
                }
            }
            else
            {
                db.Add(new InterProductSpecificUnit { ProductId = productId, UnitId = unit, SortOrder = i, TenantId = request.TenantId, OwnerId = product.OwnerId });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await ProductAsync(productId, language, ct).ConfigureAwait(false))!;
    }

    public async Task<IReadOnlyList<ProductUnitDto>?> TypeAsync(Guid typeId, string language, CancellationToken ct)
    {
        if (!await TypeVisibleAsync(typeId, ct).ConfigureAwait(false))
        {
            return null;
        }

        var rows = await TypeRowsAsync(typeId, ct).ConfigureAwait(false);
        var used = await TypeUnitsInUseAsync(rows, ct).ConfigureAwait(false);
        var names = await NamesAsync(rows.Select(r => r.UnitId!.Value), language, ct).ConfigureAwait(false);
        return [.. rows.Select(r => Dto(r.UnitId!.Value, names, used.Contains(r.Id)))];
    }

    /// <summary>Saves a product type's units in the order given (configuration: an administrator); a unit a sale price points at stays.</summary>
    public async Task<IReadOnlyList<ProductUnitDto>> SaveTypeAsync(Guid typeId, IReadOnlyList<Guid> unitIds, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(unitIds);
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("rights", "Only an administrator sets a product type's units.");
        }

        if (!await TypeVisibleAsync(typeId, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("typeId", "There is no such product type.");
        }

        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var wanted = await CheckedUnitsAsync(unitIds, ct).ConfigureAwait(false);
        var rows = await TypeRowsAsync(typeId, ct, tracked: true).ConfigureAwait(false);
        var used = await TypeUnitsInUseAsync(rows, ct).ConfigureAwait(false);
        var stays = rows.Where(r => !wanted.Contains(r.UnitId!.Value) && used.Contains(r.Id)).Select(r => r.UnitId!.Value).ToList();
        if (stays.Count > 0)
        {
            var names = await NamesAsync(stays, language, ct).ConfigureAwait(false);
            throw ValidationException.For("unitIds", $"A sale price uses it, so it stays: {string.Join(", ", stays.Select(u => names.GetValueOrDefault(u).Name ?? u.ToString()))}.");
        }

        foreach (var row in rows.Where(r => !wanted.Contains(r.UnitId!.Value)))
        {
            row.Active = false;
        }

        foreach (var (unit, i) in wanted.Select((u, i) => (u, i)))
        {
            if (rows.FirstOrDefault(r => r.UnitId == unit) is { } row)
            {
                if (row.SortOrder != i)
                {
                    row.SortOrder = i;
                }
            }
            else
            {
                db.Add(new InterProductTypeUnit { ProductTypeId = typeId, UnitId = unit, SortOrder = i, TenantId = request.TenantId, OwnerId = company });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await TypeAsync(typeId, language, ct).ConfigureAwait(false))!;
    }

    /// <summary>The units a product is offered in, the default first: its own, or else its type's.</summary>
    public async Task<IReadOnlyList<Guid>> OfferedAsync(Guid productId, CancellationToken ct)
    {
        var own = await OwnAsync(productId, ct).ConfigureAwait(false);
        if (own.Count > 0)
        {
            return [.. own.Select(r => r.UnitId!.Value)];
        }

        var type = await db.Set<Product>().AsNoTracking().Where(p => p.Id == productId).Select(p => (Guid?)p.TypeId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return type is { } t ? [.. (await TypeRowsAsync(t, ct).ConfigureAwait(false)).Select(r => r.UnitId!.Value)] : [];
    }

    private async Task<List<InterProductSpecificUnit>> OwnAsync(Guid productId, CancellationToken ct) =>
        await db.Set<InterProductSpecificUnit>().AsNoTracking().Where(r => r.ProductId == productId && r.UnitId != null).OrderBy(r => r.SortOrder).ThenBy(r => r.CreatedOn)
            .ToListAsync(ct).ConfigureAwait(false);

    private async Task<List<InterProductTypeUnit>> TypeRowsAsync(Guid typeId, CancellationToken ct, bool tracked = false)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var set = tracked ? db.Set<InterProductTypeUnit>() : db.Set<InterProductTypeUnit>().AsNoTracking();
        return [.. (await set.Where(r => r.ProductTypeId == typeId && r.UnitId != null).OrderBy(r => r.SortOrder).ThenBy(r => r.CreatedOn).ToListAsync(ct).ConfigureAwait(false))
            .Where(r => r.OwnerId is null || nodes.Contains(r.OwnerId.Value))];
    }

    // Which of the product's units something points at: a row of any table with a link to the product's unit row (stock, balances, prices,
    // cost layers, assets — found through the model), or a document line with the product in that unit.
    private async Task<HashSet<Guid>> ProductUnitsInUseAsync(Guid productId, IReadOnlyList<InterProductSpecificUnit> own, CancellationToken ct)
    {
        var used = new HashSet<Guid>();
        var rows = own.ToDictionary(r => r.Id, r => r.UnitId!.Value);
        var ids = rows.Keys.ToList();
        foreach (var table in db.Model.GetEntityTypes().Where(e => e.GetForeignKeys().Any(k => k.PrincipalEntityType.ClrType == typeof(InterProductSpecificUnit))))
        {
            foreach (var key in table.GetForeignKeys().Where(k => k.PrincipalEntityType.ClrType == typeof(InterProductSpecificUnit) && k.Properties.Count == 1))
            {
                var column = key.Properties[0].Name;
                var set = (IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(table.ClrType).Invoke(db, null)!;
                var pointed = await set.AsNoTracking().Select(r => EF.Property<Guid?>(r, column)).Where(v => v != null && ids.Contains(v.Value)).Distinct()
                    .ToListAsync(ct).ConfigureAwait(false);
                used.UnionWith(pointed.Select(v => rows[v!.Value]));
            }
        }

        var units = rows.Values.ToList();
        used.UnionWith(await db.Set<Transaction>().AsNoTracking().Where(t => t.ElementId == productId && t.UnitId != null && units.Contains(t.UnitId.Value))
            .Select(t => t.UnitId!.Value).Distinct().ToListAsync(ct).ConfigureAwait(false));
        return used;
    }

    // A type's unit row a sale price for the type points at.
    private async Task<HashSet<Guid>> TypeUnitsInUseAsync(IReadOnlyList<InterProductTypeUnit> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        return [.. await db.Set<ProductSalePriceType>().AsNoTracking().Where(p => p.ProductTypeUnitId != null && ids.Contains(p.ProductTypeUnitId.Value))
            .Select(p => p.ProductTypeUnitId!.Value).ToListAsync(ct).ConfigureAwait(false)];
    }

    private async Task<List<Guid>> CheckedUnitsAsync(IReadOnlyList<Guid> unitIds, CancellationToken ct)
    {
        var wanted = unitIds.Distinct().ToList();
        var nodes = (await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false)).Concat(await visibility.NodesAsync(ct).ConfigureAwait(false)).ToHashSet();
        var known = (await db.Set<Unit>().AsNoTracking().Where(u => wanted.Contains(u.Id)).Select(u => new { u.Id, u.OwnerId }).ToListAsync(ct).ConfigureAwait(false))
            .Where(u => u.OwnerId is null || nodes.Contains(u.OwnerId.Value)).Select(u => u.Id).ToHashSet();
        if (wanted.FirstOrDefault(u => !known.Contains(u)) is { } missing && missing != Guid.Empty)
        {
            throw ValidationException.For("unitIds", $"There is no unit {missing}.");
        }

        return wanted;
    }

    private async Task<Product?> VisibleProductAsync(Guid productId, CancellationToken ct)
    {
        var product = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct).ConfigureAwait(false);
        return product is not null && (product.OwnerId is null || (await visibility.NodesAsync(ct).ConfigureAwait(false)).Contains(product.OwnerId.Value)) ? product : null;
    }

    private async Task<bool> TypeVisibleAsync(Guid typeId, CancellationToken ct)
    {
        var type = await db.Set<ProductType>().AsNoTracking().Where(t => t.Id == typeId).Select(t => new { t.OwnerId }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return type is not null && (type.OwnerId is null || (await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false)).Contains(type.OwnerId.Value));
    }

    // The register's rule: writing a product needs the write right on one of its categories.
    private async Task<bool> MayWriteAsync(Guid productId, CancellationToken ct)
    {
        foreach (var category in await db.Set<InterProductCategory>().AsNoTracking().Where(l => l.ElementId == productId).Select(l => l.CategoryId).ToListAsync(ct)
                     .ConfigureAwait(false))
        {
            if ((await visibility.RightsAsync(ProductTable, category, ct).ConfigureAwait(false)).Write)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<Dictionary<Guid, (string? Code, string? Name)>> NamesAsync(IEnumerable<Guid> ids, string language, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return (await db.Set<Unit>().AsNoTracking().Where(u => list.Contains(u.Id)).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(u => u.Id, u => (u.Code, u.Name.In(language) ?? u.Code));
    }

    private static ProductUnitDto Dto(Guid unit, Dictionary<Guid, (string? Code, string? Name)> names, bool inUse) =>
        new(unit, names.GetValueOrDefault(unit).Code, names.GetValueOrDefault(unit).Name, inUse);
}

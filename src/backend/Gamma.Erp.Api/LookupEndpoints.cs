using System.Collections.Concurrent;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Currencies;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Api;

/// <summary>
/// The choices of lookup properties (a type info's ElementsId names an elements row: an entity's table, maybe a category and a type). Every
/// entity mapped with elements registers how its elements are listed; currencies are listed from the currency service. The rights and nodes
/// are the entity's own, as in its lists.
/// </summary>
public static class LookupEndpoints
{
    /// <summary>Lists one entity's elements for a lookup: (request, category, type, search, language, page, page size).</summary>
    public delegate Task<PageDto<LookupOptionDto>> Lister(HttpContext http, Guid? categoryId, Guid? typeId, string? search, string language, int page,
        int pageSize, CancellationToken ct);

    // Filled as each app maps its entities; several apps (the tests' hosts) may map at once.
    private static readonly ConcurrentDictionary<Type, Lister> Listers = new();

    /// <summary>Called by the entity mapping: <typeparamref name="TElement"/>'s elements can be picked by a lookup.</summary>
    public static void Register<TElement>(Lister lister) => Listers[typeof(TElement)] = lister;

    public static void MapLookups(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var lookups = api.MapGroup("/lookups").WithTags("lookups");

        // The scopes a lookup property may pick from, for the property editor: the elements rows of a listable table (no document conf), one per
        // table, category and type. A row naming one record (the packs' currency rows name the base currency) lists its whole scope: a lookup
        // ignores the record (BUILD-PLAN note 21), so the row without a record is preferred when there is one.
        lookups.MapGet("/", async (ErpDbContext db, string? language, CancellationToken ct) =>
        {
            var tables = Tables(db);
            var rows = await db.Set<Elements>().AsNoTracking().Where(e => e.Table != null && e.DocumentConf == null).ToListAsync(ct).ConfigureAwait(false);
            return rows.Where(e => tables.ContainsKey(e.Table!))
                .GroupBy(e => (e.Table, e.CategoryId, e.TypeId)).Select(g => g.OrderBy(e => e.RecordId is not null).ThenBy(e => e.Id).First())
                .Select(e => new LookupScopeDto(e.Id, e.Table!, e.CategoryId, e.TypeId, e.Name?.In(language ?? "en") ?? ""))
                .OrderBy(e => e.Table, StringComparer.Ordinal).ThenBy(e => e.CategoryId is not null).ThenBy(e => e.Name, StringComparer.CurrentCulture)
                .ToList();
        });

        lookups.MapGet("/{id:guid}", async (HttpContext http, ErpDbContext db, Guid id, string? search, string? language, int? page, int? pageSize,
            CancellationToken ct) =>
        {
            var scope = await db.Set<Elements>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
            if (scope?.Table is null || !Tables(db).TryGetValue(scope.Table, out var lister))
            {
                return Results.NotFound();
            }

            return Results.Ok(await lister(http, scope.CategoryId, scope.TypeId, search, language ?? "en", page ?? 1, pageSize ?? 50, ct).ConfigureAwait(false));
        });
    }

    /// <summary>The listable tables by their name in the database: every registered entity, and currencies.</summary>
    private static Dictionary<string, Lister> Tables(ErpDbContext db)
    {
        var tables = Listers.ToDictionary(l => db.Model.FindEntityType(l.Key)!.GetTableName()!, l => l.Value, StringComparer.Ordinal);
        tables[db.Model.FindEntityType(typeof(Currency))!.GetTableName()!] = CurrenciesAsync;
        return tables;
    }

    // Currencies have no categories or types: the active ones the request uses, matching the code or name, by code.
    private static async Task<PageDto<LookupOptionDto>> CurrenciesAsync(HttpContext http, Guid? categoryId, Guid? typeId, string? search, string language,
        int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = (Math.Max(page, 1), Math.Clamp(pageSize, 1, 500));
        var all = await http.RequestServices.GetRequiredService<CurrencyService>().CurrenciesAsync(language, ct).ConfigureAwait(false);
        var term = search?.Trim();
        var options = all.Select(c => new LookupOptionDto(c.Id, c.Code, c.Name ?? ""))
            .Where(o => string.IsNullOrEmpty(term) || (o.Code?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || o.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(o => o.Code, StringComparer.Ordinal).ToList();
        return new PageDto<LookupOptionDto>([.. options.Skip((page - 1) * pageSize).Take(pageSize)], options.Count, page, pageSize);
    }
}

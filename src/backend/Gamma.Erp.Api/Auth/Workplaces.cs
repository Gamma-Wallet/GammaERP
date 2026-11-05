using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Api.Auth;

/// <summary>
/// Where a user may work (the founder, 2026-10-31): the cost centres linked to them and every one below those, never above. The entry page
/// offers the linked ones and the companies below them (a type that keeps books), not the cost centres inside a company.
/// </summary>
public static class Workplaces
{
    /// <summary>Whether a node is one of the linked ones or below one of them.</summary>
    public static async Task<bool> MayWorkAtAsync(ErpDbContext db, IReadOnlyCollection<Guid> linked, Guid node, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(linked);
        if (linked.Contains(node))
        {
            return true;
        }

        var parents = await db.Set<CostCenter>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.IdSup, ct).ConfigureAwait(false);
        var seen = new HashSet<Guid>();
        for (var up = parents.GetValueOrDefault(node); up is { } u && seen.Add(u); up = parents.GetValueOrDefault(u))
        {
            if (linked.Contains(u))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The linked cost centres and the companies below them, each once, the linked ones first, then down the tree.</summary>
    public static async Task<IReadOnlyList<(CostCenter Place, bool Linked)>> OfferedAsync(ErpDbContext db, IReadOnlyCollection<Guid> linked, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(linked);
        var all = await db.Set<CostCenter>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var types = await db.Set<CostCenterType>().AsNoTracking().ToDictionaryAsync(t => t.Id, t => (t.IdSup, t.KeepsBooks), ct).ConfigureAwait(false);
        bool Workplace(CostCenter c) => types.TryGetValue(c.TypeId, out var t) && t.KeepsBooks;
        var children = all.Where(c => c.IdSup is not null).ToLookup(c => c.IdSup!.Value);
        var offered = new List<(CostCenter, bool)>();
        var seen = new HashSet<Guid>();
        void Walk(CostCenter place, bool isLinked)
        {
            if (!seen.Add(place.Id))
            {
                return;
            }

            offered.Add((place, isLinked));
            foreach (var child in children[place.Id].Where(Workplace).OrderBy(c => c.Code, StringComparer.Ordinal))
            {
                Walk(child, linked.Contains(child.Id));
            }
        }

        foreach (var place in all.Where(c => linked.Contains(c.Id)).OrderBy(c => c.Code, StringComparer.Ordinal))
        {
            Walk(place, true);
        }

        return offered;
    }
}

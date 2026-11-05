using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Entities;

/// <summary>What a request may see and do: the nodes below it, and its rights per entity and category.</summary>
public sealed record Rights(bool Write, bool Forward, bool ReadAll, bool ReadOwn)
{
    public static readonly Rights None = new(false, false, false, false);

    public static readonly Rights All = new(true, true, true, true);

    /// <summary>Read all, write or forward show every element of the category; read own only those given to the user.</summary>
    public bool SeesAll => ReadAll || Write || Forward;

    public Rights Or(Rights other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(Write || other.Write, Forward || other.Forward, ReadAll || other.ReadAll, ReadOwn || other.ReadOwn);
    }
}

/// <summary>
/// The node rule and the rights, read once per request:
/// <list type="bullet">
/// <item>a request at a node sees rows owned by that node and the nodes below it, and the organisation's own definitions (no owner) —
/// never what is above it;</item>
/// <item>rights are per user and per role (a user category), per entity and category; a user's rights are
/// those given to the user and to each of the user's roles together. An administrator has them all.</item>
/// </list>
/// </summary>
public sealed class Visibility(ErpDbContext db, RequestContext request)
{
    private HashSet<Guid>? _nodes;
    private string? _language;
    private Dictionary<Guid, Guid?>? _parents;
    private HashSet<Guid>? _companies;

    /// <summary>
    /// The request's node and every node below it, stopping at a company below it (the founder, 2026-10-31): a group sees its own rows and
    /// its own cost centres, never its companies'; a company sees its whole tree. What passes between them goes by documents.
    /// </summary>
    public async Task<HashSet<Guid>> NodesAsync(CancellationToken ct)
    {
        if (_nodes is not null)
        {
            return _nodes;
        }

        var parents = await ParentsAsync(ct).ConfigureAwait(false);
        var companies = await CompaniesAsync(ct).ConfigureAwait(false);
        var children = parents.Where(c => c.Value is not null).ToLookup(c => c.Value!.Value, c => c.Key);
        var nodes = new HashSet<Guid> { request.NodeId };
        var queue = new Queue<Guid>([request.NodeId]);
        while (queue.TryDequeue(out var node))
        {
            foreach (var child in children[node].Where(c => !companies.Contains(c) && nodes.Add(c)))
            {
                queue.Enqueue(child);
            }
        }

        return _nodes = nodes;
    }

    /// <summary>
    /// The nodes whose configuration (categories, types, scenarios…) the request uses: its own and those below it, and the nodes above it up
    /// to its company, because a company's configuration applies to all its cost centres. Never the corporate's or another company's.
    /// </summary>
    public async Task<HashSet<Guid>> ConfigurationNodesAsync(CancellationToken ct)
    {
        var nodes = new HashSet<Guid>(await NodesAsync(ct).ConfigureAwait(false));
        var parents = await ParentsAsync(ct).ConfigureAwait(false);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        for (Guid? node = request.NodeId; company is not null && node is { } n && parents.TryGetValue(n, out var parent); node = parent)
        {
            nodes.Add(n);
            if (n == company)
            {
                break;
            }
        }

        return nodes;
    }

    /// <summary>
    /// The company the request's node is in: the node itself or the nearest node above it that keeps its own books (a company is the root of
    /// its tree or directly under it, e.g. a barber shop that is its own root, or Fast Food Tirana under Gamma Group). At a group (a corporate)
    /// the group itself: it behaves as a company with its own books (the founder, 2026-10-31).
    /// </summary>
    public async Task<Guid?> CompanyAsync(CancellationToken ct)
    {
        var parents = await ParentsAsync(ct).ConfigureAwait(false);
        var companies = await CompaniesAsync(ct).ConfigureAwait(false);
        for (Guid? node = request.NodeId; node is { } n && parents.ContainsKey(n); node = parents[n])
        {
            if (companies.Contains(n))
            {
                return n;
            }
        }

        return null;
    }

    /// <summary>The nodes that keep their own books: those whose type does, and every root (a group behaves as a company).</summary>
    public async Task<HashSet<Guid>> CompaniesAsync(CancellationToken ct) =>
        _companies ??= [.. await (from c in db.Set<CostCenter>().AsNoTracking() join t in db.Set<CostCenterType>().AsNoTracking() on c.TypeId equals t.Id
                                  where t.KeepsBooks || c.IdSup == null select c.Id).ToListAsync(ct).ConfigureAwait(false)];

    private async Task<Dictionary<Guid, Guid?>> ParentsAsync(CancellationToken ct) =>
        _parents ??= await db.Set<CostCenter>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.IdSup, ct).ConfigureAwait(false);

    /// <summary>
    /// The company's menu and the nodes of it the request may see (Menu access): an administrator every node; anyone
    /// else the nodes given to them or to one of their roles, everything under those, and the folders above them, never a node marked for administrators.
    /// </summary>
    public async Task<(List<Menu> Menu, HashSet<Guid> Shown)> MenuAsync(CancellationToken ct)
    {
        var nodes = await ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var menu = await db.Set<Menu>().AsNoTracking().Where(m => m.OwnerId != null && nodes.Contains(m.OwnerId.Value)).ToListAsync(ct).ConfigureAwait(false);
        if (request.IsAdministrator)
        {
            return (menu, menu.Select(m => m.Id).ToHashSet());
        }

        menu = [.. menu.Where(m => !m.AdminOnly)];
        var ids = menu.Select(m => m.Id).ToHashSet();

        var children = menu.ToLookup(m => m.IdSup is { } p && ids.Contains(p) ? p : (Guid?)null);
        var roles = request.RoleIds.ToList();
        var given = await db.Set<InterMenuUser>().AsNoTracking()
            .Where(l => l.MenuId != null && ((l.UserId != null && l.UserId == request.UserId) || (l.RoleId != null && roles.Contains(l.RoleId.Value))))
            .Select(l => l.MenuId!.Value).ToListAsync(ct).ConfigureAwait(false);
        var byId = menu.ToDictionary(m => m.Id);
        var shown = new HashSet<Guid>();
        var below = new Stack<Guid>(given.Where(ids.Contains));
        while (below.TryPop(out var id))
        {
            if (shown.Add(id))
            {
                foreach (var child in children[id])
                {
                    below.Push(child.Id);
                }
            }
        }

        // The folders above: each shown node climbs until it meets one already shown (whose own climb adds the rest).
        foreach (var id in shown.ToList())
        {
            var up = byId[id].IdSup;
            while (up is { } p && byId.ContainsKey(p) && shown.Add(p))
            {
                up = byId[p].IdSup;
            }
        }

        return (menu, shown);
    }

    /// <summary>The language of the company the request works in (of the group above the companies); English when none is set.</summary>
    public async Task<string> CompanyLanguageAsync(CancellationToken ct)
    {
        if (_language is null)
        {
            var parents = await ParentsAsync(ct).ConfigureAwait(false);
            var line = new List<Guid>();
            for (Guid? node = request.NodeId; node is { } n && parents.ContainsKey(n) && !line.Contains(n); node = parents[n])
            {
                line.Add(n);
            }

            var languages = await db.Set<Gamma.Erp.Models.CostCenters.CostCenter>().AsNoTracking().Where(c => line.Contains(c.Id) && c.Language != null)
                .ToDictionaryAsync(c => c.Id, c => c.Language!, ct).ConfigureAwait(false);
            _language = line.Select(languages.GetValueOrDefault).FirstOrDefault(l => l is not null) ?? TranslatedText.DefaultLanguage;
        }

        return _language;
    }

    /// <summary>Whether the request's menu holds a screen: Menu access decides who opens it, on the server too.</summary>
    public Task<bool> OpensScreenAsync(string screen, CancellationToken ct) => OpensScreenAsync([screen], [], ct);

    /// <summary>Whether the request's menu holds one of these screens, with one of these parameters if any are named. An administrator opens everything.</summary>
    public async Task<bool> OpensScreenAsync(IReadOnlyCollection<string> screens, IReadOnlyCollection<string> parameters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(parameters);
        if (request.IsAdministrator)
        {
            return true;
        }

        var (menu, shown) = await MenuAsync(ct).ConfigureAwait(false);
        return menu.Any(m => m.Screen is { } screen && screens.Contains(screen) && shown.Contains(m.Id) && (parameters.Count == 0 || (m.Parameter is { } parameter && parameters.Contains(parameter))));
    }

    /// <summary>The request's rights on an entity (its row in tbl_elements, named by its table) and one of its categories.</summary>
    public async Task<Rights> RightsAsync(string table, Guid categoryId, CancellationToken ct)
    {
        if (request.IsAdministrator)
        {
            return Rights.All;
        }

        // Every elements row naming the whole entity (no category, type or record) stands for it: a corporate's pack brings one.
        var entity = await db.Set<Elements>().AsNoTracking().Where(e => e.Table == table && e.CategoryId == null && e.TypeId == null && e.RecordId == null)
            .Select(e => e.Id).ToListAsync(ct).ConfigureAwait(false);
        var rows = await db.Set<ElementsAccess>().AsNoTracking()
            .Where(a => a.ElementsId != null && entity.Contains(a.ElementsId.Value) && a.CategoryId == categoryId
                && ((a.UserId != null && a.UserId == request.UserId) || (a.RoleId != null && request.RoleIds.Contains(a.RoleId.Value))))
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Aggregate(Rights.None, (r, a) => r.Or(new Rights(a.Write, a.Forward, a.ReadAll, a.ReadOwn)));
    }
}

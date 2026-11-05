using System.Reflection;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Seed;

/// <summary>
/// A new company's configuration copied from another company of the same corporate instead of loaded from a pack: the rows the source
/// company owns of the models a pack is made of, the base always and the groups chosen (chart, documents, posting, depreciation), each with a
/// new id, owned by the new company. Every link between copied rows is moved onto the copies; links to the organisation's own rows (the
/// Company type, the Companies category) stay as they are. As when a pack loads, a row that points at a row of the source company not copied
/// (a group left out, a deleted row) is left out too, and so on down. The source's own changes (history copies) are not copied.
/// </summary>
public static class CompanyCopy
{
    private static readonly MethodInfo SetOf = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;

    /// <summary>Adds the copies to <paramref name="db"/> (saving is the caller's) and returns how many rows were copied.</summary>
    public static async Task<int> AddAsync(ErpDbContext db, Guid tenantId, Guid fromCompany, Guid toCompany, IReadOnlyCollection<string>? groups, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var chosen = StarterPacks.Chosen(groups);
        var groupOf = StarterPacks.Groups.SelectMany(g => g.Value.Models.Select(m => (m, g.Key))).ToDictionary(x => x.m, x => x.Key, StringComparer.Ordinal);

        var candidates = new List<Row>();
        var left = new HashSet<Guid>();
        foreach (var (model, type) in StarterPacks.PackModels)
        {
            var set = (IQueryable<Row>)SetOf.MakeGenericMethod(type).Invoke(db, null)!;
            var rows = await set.IgnoreQueryFilters().AsNoTracking().Where(r => r.OwnerId == fromCompany).ToListAsync(ct).ConfigureAwait(false);
            var taken = !groupOf.TryGetValue(model, out var group) || chosen.Contains(group);
            foreach (var row in rows)
            {
                if (taken && row.Active)
                {
                    candidates.Add(row);
                }
                else
                {
                    left.Add(row.Id);
                }
            }
        }

        var links = candidates.Select(r => r.GetType()).Distinct().ToDictionary(t => t, Links);
        for (var grew = true; grew;)
        {
            var more = candidates.Where(r => !left.Contains(r.Id) && links[r.GetType()].Any(p => !StarterPacks.IsSoftLink(p.Name) && p.GetValue(r) is Guid v && left.Contains(v)))
                .Select(r => r.Id).ToList();
            left.UnionWith(more);
            grew = more.Count > 0;
        }

        var copied = candidates.Where(r => !left.Contains(r.Id)).ToList();
        var ids = copied.ToDictionary(r => r.Id, _ => Guid.CreateVersion7());
        foreach (var row in copied)
        {
            var copy = (Row)Activator.CreateInstance(row.GetType(), nonPublic: true)!;
            foreach (var property in Columns(row.GetType()))
            {
                property.SetValue(copy, Clone(property.GetValue(row)));
            }

            foreach (var link in links[row.GetType()])
            {
                if (link.GetValue(row) is Guid v && ids.TryGetValue(v, out var moved))
                {
                    link.SetValue(copy, moved);
                }
                else if (link.GetValue(row) is Guid gone && left.Contains(gone) && StarterPacks.IsSoftLink(link.Name))
                {
                    link.SetValue(copy, null);
                }
            }

            (copy.Id, copy.TenantId, copy.OwnerId, copy.IdChange, copy.CreatedBy) = (ids[row.Id], tenantId, toCompany, null, null);
            db.Add(copy);
        }

        return copied.Count;
    }

    /// <summary>The row's own columns: every public property it can be written with.</summary>
    private static IEnumerable<PropertyInfo> Columns(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p is { CanRead: true, CanWrite: true } && p.GetIndexParameters().Length == 0);

    /// <summary>The row's links to other rows: its id columns other than its own id, its corporate, its owner and its history.</summary>
    private static List<PropertyInfo> Links(Type type) =>
        [.. Columns(type).Where(p => (p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?))
            && p.Name is not (nameof(Row.Id) or nameof(Row.TenantId) or nameof(Row.OwnerId) or nameof(Row.IdChange) or nameof(Row.CreatedBy)))];

    private static object? Clone(object? value) => value switch
    {
        TranslatedText text => new TranslatedText { Values = new Dictionary<string, string>(text.Values, StringComparer.OrdinalIgnoreCase) },
        JsonNode node => node.DeepClone(),
        _ => value,
    };
}

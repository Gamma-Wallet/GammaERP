using Gamma.Erp.Models.Platform;

namespace Gamma.Erp.Services.Migration;

/// <summary>A step in the tree: how deep it sits, the steps it names itself, every step above it, and the step it shows under.</summary>
public sealed record MigrationNode(MigrationItem Item, int Level, IReadOnlyList<string> Needs, IReadOnlySet<string> Above, string? Parent = null);

/// <summary>
/// The dependency tree of the steps, from what each step's configuration names (<see cref="MigrationItem.Requires"/>): a step sits under the deepest step it
/// needs, and comes after everything it needs. A step naming no step that exists, or a loop, is a configuration error, not an order to guess.
/// </summary>
public static class MigrationTree
{
    public static IReadOnlyList<MigrationNode> Build(IReadOnlyList<MigrationItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var byCode = items.ToDictionary(i => i.Code!, StringComparer.Ordinal);
        var needs = items.ToDictionary(i => i.Code!, Needs, StringComparer.Ordinal);
        foreach (var (code, names) in needs)
        {
            if (names.FirstOrDefault(n => !byCode.ContainsKey(n)) is { } unknown)
            {
                throw ValidationException.For("step", $"{ErrorCodes.NotConfiguredError}: the step {code} needs {unknown}, which is no step.");
            }
        }

        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        var above = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        int Walk(string code, Stack<string> path)
        {
            if (levels.TryGetValue(code, out var known))
            {
                return known;
            }

            if (path.Contains(code))
            {
                throw ValidationException.For("step", $"{ErrorCodes.NotConfiguredError}: the steps need each other: {string.Join(" > ", path.Reverse().Append(code))}.");
            }

            path.Push(code);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var level = 0;
            foreach (var need in needs[code])
            {
                level = Math.Max(level, Walk(need, path) + 1);
                seen.Add(need);
                seen.UnionWith(above[need]);
            }

            path.Pop();
            above[code] = seen;
            return levels[code] = level;
        }

        foreach (var code in needs.Keys)
        {
            Walk(code, new Stack<string>());
        }

        // Each step under the deepest step it needs; a parent's whole branch comes before the next, so nothing shows before what it needs.
        var parent = items.ToDictionary(i => i.Code!, i => needs[i.Code!].OrderByDescending(n => levels[n]).FirstOrDefault(), StringComparer.Ordinal);
        var children = items.OrderBy(i => i.SortOrder).ThenBy(i => i.Code, StringComparer.Ordinal).ToLookup(i => parent[i.Code!] ?? string.Empty, StringComparer.Ordinal);
        var ordered = new List<MigrationNode>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var waiting = new List<MigrationItem>(children[string.Empty]);
        void Place(MigrationItem item)
        {
            var code = item.Code!;
            if (!placed.Add(code))
            {
                return;
            }

            // Whatever else it needs comes first, wherever that sits.
            foreach (var need in needs[code])
            {
                Place(byCode[need]);
            }

            ordered.Add(new(item, levels[code], needs[code], above[code]));
            foreach (var child in children[code])
            {
                Place(child);
            }
        }

        foreach (var root in waiting)
        {
            Place(root);
        }

        // It shows under the last of what it needs to come ready, and the tree is read depth first.
        var place = ordered.Select((n, i) => (n.Item.Code!, i)).ToDictionary(x => x.Item1, x => x.i, StringComparer.Ordinal);
        var under = ordered.ToDictionary(n => n.Item.Code!, n => n.Needs.OrderByDescending(c => place[c]).FirstOrDefault(), StringComparer.Ordinal);
        var branches = ordered.ToLookup(n => under[n.Item.Code!] ?? string.Empty, StringComparer.Ordinal);
        var tree = new List<MigrationNode>();
        void Show(MigrationNode node)
        {
            tree.Add(node with { Parent = under[node.Item.Code!] });
            foreach (var child in branches[node.Item.Code!])
            {
                Show(child);
            }
        }

        foreach (var root in branches[string.Empty])
        {
            Show(root);
        }

        return tree;
    }

    private static string[] Needs(MigrationItem item) => (item.Requires ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

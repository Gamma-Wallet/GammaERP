using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Persistence;

namespace Gamma.Erp.Seed;

/// <summary>
/// The configuration a company starts with, per country (Packs/&lt;country&gt;/&lt;Model&gt;.json). In a pack every row has a key and refers
/// to other rows by their keys ("document_type:SALES_INVOICE_GOODS"); loading gives each key a new id, so the same pack can be loaded onto many companies.
/// Keys the pack only refers to (the company's own type and category, which belong to the organisation) are given by the caller.
/// Every row loaded belongs to the company (<see cref="Row.OwnerId"/>), so only the company and its cost centres see it.
/// <para>A pack loads in groups (Packs/_catalogue/groups.json: chart, documents, posting, depreciation; the base, everything else, always),
/// so a company may take some from the pack and copy others from another company; a group needs the groups it requires. A profile
/// (Packs/&lt;country&gt;/profiles/&lt;activity&gt;.json) keeps, of each model it names, only the rows with the codes it lists (with their
/// parents and the reversal types of the document types it keeps). A row that points at a row not loaded (a group left out, a row the
/// profile drops) is left out too, and so on down: a document type dropped takes its links, fills, sides and scenarios with it.</para>
/// </summary>
public static class StarterPacks
{
    private static readonly Assembly Resources = typeof(StarterPacks).Assembly;

    /// <summary>The embedded pack files by their path ("Packs/AL/DocumentType.json"), whatever separator the build used.</summary>
    internal static readonly Dictionary<string, string> Files = Resources.GetManifestResourceNames()
        .Where(n => n.StartsWith("Packs", StringComparison.Ordinal)).ToDictionary(n => n.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/'), n => n, StringComparer.Ordinal);

    private static readonly Dictionary<string, Type> Models = typeof(Row).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(Row).IsAssignableFrom(t))
        .ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>The countries that have a pack.</summary>
    public static IReadOnlyList<string> Countries =>
        [.. Files.Keys.Select(n => n.Split('/')[1]).Where(c => !c.StartsWith('_')).Distinct().Order()];

    /// <summary>The organisation rows a pack refers to without containing them: pack key → what the caller must give (e.g. "CompanyType").</summary>
    public static IReadOnlyDictionary<string, string> External(string country) =>
        Read(country, "_external") is JsonObject o ? o.ToDictionary(p => p.Key, p => p.Value!.GetValue<string>(), StringComparer.Ordinal) : [];

    /// <summary>
    /// Adds the pack of <paramref name="country"/> to <paramref name="db"/> for one company (saving is the caller's), and returns how many
    /// rows it added and the id each pack key got. <paramref name="organisation"/> gives the ids of the organisation rows the pack refers
    /// to, by the names in <see cref="External"/>.
    /// </summary>
    public static (int Count, IReadOnlyDictionary<string, Guid> Ids) Add(ErpDbContext db, string country, Guid tenantId, Guid companyId, IReadOnlyDictionary<string, Guid> organisation,
        IReadOnlyCollection<string>? groups = null, string? profile = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(organisation);
        var chosen = Chosen(groups);
        var models = Names(country).Select(m => (Model: m, Rows: Read(country, m)!.AsArray().OfType<JsonObject>().ToList())).ToList();
        var left = LeftOut(models, chosen, profile is null ? null : Read(country, $"profiles/{profile}") as JsonObject
            ?? throw new ArgumentException($"The {country} pack has no profile {profile}.", nameof(profile)));
        var ids = External(country).ToDictionary(e => e.Key, e => organisation.TryGetValue(e.Value, out var id) ? id
            : throw new ArgumentException($"The {country} pack needs the organisation's {e.Value}.", nameof(organisation)), StringComparer.Ordinal);
        Guid Id(string key) => ids.TryGetValue(key, out var id) ? id : ids[key] = Guid.CreateVersion7();

        var count = 0;
        foreach (var (model, rows) in models)
        {
            var type = Models.GetValueOrDefault(model) ?? throw new InvalidOperationException($"The {country} pack has rows of an unknown model {model}.");
            foreach (var row in rows.Where(r => !left.Contains(KeyOf(r))))
            {
                var item = (Row)Activator.CreateInstance(type, nonPublic: true)!;
                foreach (var (name, value) in row)
                {
                    if (name == "Key")
                    {
                        item.Id = Id(value!.GetValue<string>());
                        continue;
                    }

                    var property = type.GetProperty(name) ?? throw new InvalidOperationException($"{model} has no {name} (pack {country}).");
                    if (value is null && property.PropertyType == typeof(Guid))
                    {
                        throw new InvalidOperationException($"{model} {row["Key"]} has no {name}, which it needs (pack {country}).");
                    }

                    var soft = IsSoftLink(name) && value is JsonValue v && v.TryGetValue<string>(out var target) && left.Contains(target);
                    property.SetValue(item, soft ? null : Convert(value, property.PropertyType, Id));
                }

                (item.TenantId, item.OwnerId, item.Active) = (tenantId, companyId, true);
                db.Add(item);
                count++;
            }
        }

        return (count, ids);
    }

    /// <summary>The groups asked for (all when none are named), checked: each exists and has the groups it needs.</summary>
    public static HashSet<string> Chosen(IReadOnlyCollection<string>? groups)
    {
        var chosen = new HashSet<string>(groups ?? Groups.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var group in chosen)
        {
            if (!Groups.TryGetValue(group, out var needs))
            {
                throw new ArgumentException($"There is no pack group {group}.", nameof(groups));
            }

            if (needs.Requires.FirstOrDefault(r => !chosen.Contains(r)) is { } missing)
            {
                throw new ArgumentException($"The {group} group needs the {missing} group of the same pack.", nameof(groups));
            }
        }

        return chosen;
    }

    /// <summary>Every model a pack holds rows of (of any country), with its class: what a company's configuration is made of.</summary>
    public static IReadOnlyDictionary<string, Type> PackModels { get; } = Files.Keys
        .Where(n => n.Count(c => c == '/') == 2 && !n.Split('/')[1].StartsWith('_') && !Path.GetFileName(n).StartsWith('_'))
        .Select(n => Path.GetFileNameWithoutExtension(n)).Distinct(StringComparer.Ordinal)
        .ToDictionary(m => m, m => Models.GetValueOrDefault(m) ?? throw new InvalidOperationException($"A pack has rows of an unknown model {m}."), StringComparer.Ordinal);

    /// <summary>The country's base currency (the pack's currency marked as base), e.g. "EUR"; null without a pack.</summary>
    public static string? BaseCurrency(string country) =>
        Read(country.ToUpperInvariant(), "Currency") is JsonArray rows ? rows.OfType<JsonObject>().FirstOrDefault(r => r["IsBase"]?.GetValue<bool>() == true)?["Code"]?.GetValue<string>() : null;

    /// <summary>The languages the country's pack is written in besides English (from its chart's names), e.g. ["lt"].</summary>
    public static IReadOnlyList<string> Languages(string country) =>
        Read(country.ToUpperInvariant(), "Chart") is JsonArray rows && rows.OfType<JsonObject>().FirstOrDefault()?["Name"] is JsonObject name
            ? [.. name.Select(p => p.Key).Where(k => k != TranslatedText.DefaultLanguage)] : [];

    /// <summary>The country's language: the one a company there works in with English.</summary>
    public static string Language(string country) => Languages(country) is [var language, ..] ? language : TranslatedText.DefaultLanguage;

    /// <summary>The activity profiles the country's pack has (Packs/&lt;country&gt;/profiles), e.g. ["BEAUTY", "RETAIL"].</summary>
    public static IReadOnlyList<string> Profiles(string country) =>
        [.. Files.Keys.Where(n => n.StartsWith($"Packs/{country.ToUpperInvariant()}/profiles/", StringComparison.Ordinal)).Select(n => Path.GetFileNameWithoutExtension(n)).Order()];

    /// <summary>The pack groups by name (Packs/_catalogue/groups.json): their models and the groups they need.</summary>
    public static IReadOnlyDictionary<string, (IReadOnlyList<string> Models, IReadOnlyList<string> Requires)> Groups { get; } = LoadGroups();

    private static Dictionary<string, (IReadOnlyList<string> Models, IReadOnlyList<string> Requires)> LoadGroups()
    {
        var groups = Read("_catalogue", "groups") as JsonObject ?? throw new InvalidOperationException("The pack groups are missing.");
        return groups.ToDictionary(g => g.Key, g => ((IReadOnlyList<string>)[.. g.Value!["models"]!.AsArray().Select(m => m!.GetValue<string>())],
            (IReadOnlyList<string>)[.. g.Value!["requires"]!.AsArray().Select(m => m!.GetValue<string>())]), StringComparer.OrdinalIgnoreCase);
    }

    private static string KeyOf(JsonObject row) => row["Key"]?.GetValue<string>() ?? "";

    /// <summary>
    /// A link the row can do without (a proposed default, <c>TypeInfo.DefaultElementId</c>): when its target is left out, the row
    /// stays and the link is empty, instead of the row being left out too.
    /// </summary>
    public static bool IsSoftLink(string property) => property == nameof(Gamma.Erp.Models.Base.TypeInfo.DefaultElementId);

    /// <summary>
    /// The keys of the rows not loaded: those of the groups not chosen, those the profile drops, and every row pointing at one of them
    /// (to a fixed point).
    /// </summary>
    private static HashSet<string> LeftOut(List<(string Model, List<JsonObject> Rows)> models, HashSet<string> chosen, JsonObject? profile)
    {
        var groupOf = Groups.SelectMany(g => g.Value.Models.Select(m => (m, g.Key))).ToDictionary(x => x.m, x => x.Key, StringComparer.Ordinal);
        var left = models.Where(m => groupOf.TryGetValue(m.Model, out var g) && !chosen.Contains(g)).SelectMany(m => m.Rows).Select(KeyOf).ToHashSet(StringComparer.Ordinal);
        foreach (var (model, codes) in profile ?? [])
        {
            var rows = models.SingleOrDefault(m => m.Model == model).Rows ?? throw new InvalidOperationException($"A profile names {model}, which the pack does not have.");
            var listed = codes!.AsArray().Select(c => c!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var byKey = rows.ToDictionary(KeyOf, StringComparer.Ordinal);
            var kept = rows.Where(r => r["Code"]?.GetValue<string>() is { } code && listed.Contains(code)).Select(KeyOf).ToHashSet(StringComparer.Ordinal);
            // A kept row brings its parents and, for a document type, the reversal type that reverses it (and so on).
            for (var grew = true; grew;)
            {
                var more = kept.Select(k => byKey[k]["IdSup"]?.GetValue<string>())
                    .Concat(byKey.Where(r => r.Value["ReversedDocumentTypeId"]?.GetValue<string>() is { } of && kept.Contains(of)).Select(r => r.Key))
                    .OfType<string>().Where(k => byKey.ContainsKey(k) && !kept.Contains(k)).ToList();
                kept.UnionWith(more);
                grew = more.Count > 0;
            }

            left.UnionWith(byKey.Keys.Where(k => !kept.Contains(k)));
        }

        for (var grew = true; grew;)
        {
            var more = models.SelectMany(m => m.Rows).Where(r => !left.Contains(KeyOf(r))
                && r.Any(p => p.Key != "Key" && !IsSoftLink(p.Key) && p.Value is JsonValue v && v.TryGetValue<string>(out var s) && left.Contains(s))).Select(KeyOf).ToList();
            left.UnionWith(more);
            grew = more.Count > 0;
        }

        return left;
    }

    private static object? Convert(JsonNode? value, Type target, Func<string, Guid> id)
    {
        var plain = Nullable.GetUnderlyingType(target) ?? target;
        if (value is null)
        {
            return target == typeof(TranslatedText) ? null : plain.IsValueType && Nullable.GetUnderlyingType(target) is null ? Activator.CreateInstance(plain) : null;
        }

        if (plain == typeof(Guid))
        {
            return id(value.GetValue<string>());
        }

        if (plain == typeof(TranslatedText))
        {
            return new TranslatedText { Values = new Dictionary<string, string>(value.Deserialize<Dictionary<string, string>>()!, StringComparer.OrdinalIgnoreCase) };
        }

        if (plain.IsEnum)
        {
            return Enum.ToObject(plain, value.GetValue<int>());
        }

        if (plain == typeof(JsonNode))
        {
            return value.DeepClone();
        }

        return value.Deserialize(plain);
    }

    private static IEnumerable<string> Names(string country) =>
        Files.Keys.Where(n => n.StartsWith($"Packs/{country}/", StringComparison.Ordinal) && !n.Contains("/_", StringComparison.Ordinal) && n.Count(c => c == '/') == 2)
            .Select(n => Path.GetFileNameWithoutExtension(n)).Order(StringComparer.Ordinal);

    private static JsonNode? Read(string country, string model)
    {
        using var stream = Files.TryGetValue($"Packs/{country}/{model}.json", out var name) ? Resources.GetManifestResourceStream(name) : null;
        return stream is null ? null : JsonNode.Parse(stream);
    }
}

using System.Text.Json;

namespace Gamma.Erp.Seed;

/// <summary>An activity of the catalogue: its code, the activity it belongs to, its name per language.</summary>
public sealed record Activity(string Code, string? Parent, IReadOnlyDictionary<string, string> Name);

/// <summary>
/// The activity catalogue (Gamma's, shipped with the packs: Packs/_catalogue/activities.json), a tree chosen level by level: General → Retail
/// → Supermarket… A company's activity picks its configuration: the profile of the nearest level (the activity itself, then its parents)
/// that has one for the company's country (Packs/&lt;country&gt;/profiles/&lt;code&gt;.json); with none, General: the country's full configuration.
/// </summary>
public static class ActivityCatalogue
{
    public const string General = "GENERAL";

    public static IReadOnlyList<Activity> All { get; } = Load();

    /// <summary>The activity and the ones above it, from the top (General) down to it.</summary>
    public static IReadOnlyList<Activity> PathOf(string code)
    {
        var byCode = All.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
        var path = new List<Activity>();
        for (var at = byCode.GetValueOrDefault(code); at is not null; at = at.Parent is { } p ? byCode.GetValueOrDefault(p) : null)
        {
            path.Insert(0, at);
        }

        return path.Count > 0 ? path : throw new ArgumentException($"The catalogue has no activity {code}.", nameof(code));
    }

    /// <summary>The profile a company of this activity and country takes (the nearest level that has one), or null for the full configuration.</summary>
    public static string? ProfileFor(string country, string code)
    {
        ArgumentNullException.ThrowIfNull(country);
        return PathOf(code).Select(a => a.Code).Reverse().TakeWhile(c => !string.Equals(c, General, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(c => StarterPacks.Files.ContainsKey($"Packs/{country.ToUpperInvariant()}/profiles/{c}.json"));
    }

    private static List<Activity> Load()
    {
        var name = StarterPacks.Files.GetValueOrDefault("Packs/_catalogue/activities.json") ?? throw new InvalidOperationException("The activity catalogue is missing.");
        using var stream = typeof(ActivityCatalogue).Assembly.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<List<Activity>>(stream) ?? [];
    }
}

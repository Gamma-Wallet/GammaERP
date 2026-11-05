using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp;

/// <summary>
/// The user guide (.md, embedded in the API) for Claude (the founder, 2026-11-04): as resources gamma-erp://guide/&lt;path&gt;, one per page, and as two
/// read-only tools, because a model reaches for a tool more readily than for a resource. The guide says how each screen works, what it refuses and why.
/// </summary>
[McpServerToolType]
public sealed class GuideTools
{
    public const string Scheme = "gamma-erp://guide/";

    /// <summary>The pages by their path ("documents/posting.md"), read once.</summary>
    public static IReadOnlyDictionary<string, string> Pages { get; } = Load();

    [McpServerTool(Name = "search_guide", Title = "Search the user guide", ReadOnly = true)]
    [Description("Searches the Gamma ERP user guide for words (e.g. 'reverse part of a document', 'close the year'). Returns the matching pages with their title and a short excerpt; read one with read_guide. Use it before guessing how something works.")]
    public static object SearchGuide([Description("Words to look for.")] string query, [Description("At most this many pages (default 8).")] int? limit = null)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(w => w.ToLowerInvariant()).Where(w => w.Length > 2).ToList();
        if (words.Count == 0)
        {
            throw new McpException("Give one or more words to look for.");
        }

        return Pages.Select(p => (Path: p.Key, Text: p.Value, Score: words.Sum(w => Count(p.Value, w) + (p.Key.Contains(w, StringComparison.OrdinalIgnoreCase) ? 5 : 0))))
            .Where(p => p.Score > 0).OrderByDescending(p => p.Score).Take(Math.Clamp(limit ?? 8, 1, 25))
            .Select(p => new { path = p.Path, title = Title(p.Text), excerpt = Excerpt(p.Text, words) }).ToList();
    }

    [McpServerTool(Name = "read_guide", Title = "Read a guide page", ReadOnly = true)]
    [Description("Reads one page of the Gamma ERP user guide by its path (e.g. 'documents/posting.md'; 'README.md' is the index of all pages). Each page says where the screen is, who may use it, its actions with their steps, results, refusals and API.")]
    public static string ReadGuide([Description("The page's path, as search_guide or the index gives it.")] string path)
    {
        var key = path.Replace('\\', '/').TrimStart('/');
        key = key.StartsWith(Scheme, StringComparison.Ordinal) ? key[Scheme.Length..] : key;
        return Pages.TryGetValue(key, out var text) ? text : throw new McpException($"There is no page {path}: search_guide finds pages, README.md lists them all.");
    }

    /// <summary>Every page as a resource, for clients that browse resources.</summary>
    public static IEnumerable<McpServerResource> Resources() =>
        Pages.Select(p => McpServerResource.Create(() => p.Value, new McpServerResourceCreateOptions
        {
            UriTemplate = Scheme + p.Key, Name = p.Key, Title = Title(p.Value), MimeType = "text/markdown",
        }));

    private static Dictionary<string, string> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("guide/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            pages[name["guide/".Length..].Replace('\\', '/')] = reader.ReadToEnd();
        }

        return pages;
    }

    private static string Title(string text) => text.Split('\n').FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() ?? "";

    private static int Count(string text, string word)
    {
        var count = 0;
        for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(word, at + word.Length, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }

    private static string Excerpt(string text, IReadOnlyList<string> words)
    {
        var at = words.Select(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, at - 80);
        return text.Substring(start, Math.Min(240, text.Length - start)).Replace('\n', ' ').Trim();
    }
}

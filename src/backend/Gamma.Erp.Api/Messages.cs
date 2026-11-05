using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gamma.Erp.Api;

/// <summary>
/// The services' refusals in the screens' language: their English sentences are the keys (i18n/&lt;language&gt;.json), {0}, {1}… standing for the values a sentence carries.
/// One without a translation stays in English. A leading code ("StockError: …") stays as it is.
/// </summary>
public static partial class Messages
{
    private static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(200);
    private static readonly ConcurrentDictionary<string, Table?> Tables = new(StringComparer.Ordinal);

    private sealed record Table(IReadOnlyDictionary<string, string> Exact, IReadOnlyList<(Regex Pattern, string Text)> Sentences);

    public static string Translate(string message, string? language)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrEmpty(language) || language == Gamma.Erp.Models.Base.TranslatedText.DefaultLanguage || Tables.GetOrAdd(language, Load) is not { } table)
        {
            return message;
        }

        var code = CodePrefix().Match(message);
        var sentence = message[code.Length..];
        if (table.Exact.TryGetValue(sentence, out var exact))
        {
            return code.Value + exact;
        }

        foreach (var (pattern, text) in table.Sentences)
        {
            if (pattern.Match(sentence) is { Success: true } match)
            {
                return code.Value + Hole().Replace(text, h => match.Groups[$"v{h.Groups[1].Value}"].Value);
            }
        }

        return message;
    }

    private static Table? Load(string language)
    {
        using var stream = typeof(Messages).Assembly.GetManifestResourceStream($"i18n.{language}.json");
        if (stream is null)
        {
            return null;
        }

        var words = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        var sentences = words.Where(w => Hole().IsMatch(w.Key)).OrderByDescending(w => Hole().Replace(w.Key, string.Empty).Length)
            .Select(w => (new Regex("^" + Hole().Replace(Regex.Escape(w.Key).Replace(@"\{", "{", StringComparison.Ordinal), h => $"(?<v{h.Groups[1].Value}>.*)") + "$",
                RegexOptions.CultureInvariant | RegexOptions.Singleline, Patience), w.Value)).ToList();
        return new Table(words, sentences);
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Hole();

    [GeneratedRegex(@"^[A-Za-z]+Error: ")]
    private static partial Regex CodePrefix();
}

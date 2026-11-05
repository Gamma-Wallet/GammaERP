namespace Gamma.Erp.Models.Base;

/// <summary>
/// A text in every language the company uses, one value per language code ("en", "sq", "it"…), English by default.
/// </summary>
public sealed class TranslatedText
{
    public const string DefaultLanguage = "en";

    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The text in <paramref name="language"/>, else in English, else in any language there is.</summary>
    public string? In(string language) =>
        Values.TryGetValue(language, out var text) ? text
        : Values.TryGetValue(DefaultLanguage, out var english) ? english
        : Values.Values.FirstOrDefault();

    public static TranslatedText English(string text) => new() { Values = { [DefaultLanguage] = text } };

    /// <summary>A text typed once, in English and the company's language.</summary>
    public static TranslatedText Both(string text, string language) => English(text).Completed(language);

    /// <summary>This text with English and <paramref name="language"/> both there (the founder, 2026-11-02): one missing is copied from the other.</summary>
    public TranslatedText Completed(string language)
    {
        var values = new Dictionary<string, string>(Values, StringComparer.OrdinalIgnoreCase);
        if (values.Count > 0)
        {
            values[DefaultLanguage] = In(DefaultLanguage)!;
            values[language] = In(language)!;
        }

        return new TranslatedText { Values = values };
    }

    /// <summary>Completes every text of a row, so whatever is saved has its names in both languages.</summary>
    public static void Complete(object row, string language)
    {
        ArgumentNullException.ThrowIfNull(row);
        foreach (var property in row.GetType().GetProperties().Where(p => p.PropertyType == typeof(TranslatedText) && p.CanWrite))
        {
            if (property.GetValue(row) is TranslatedText text)
            {
                property.SetValue(row, text.Completed(language));
            }
        }
    }
}

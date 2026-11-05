using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Services.Entities;

/// <summary>
/// Checks a property value against its kind (founder, 2026-10-29): 1 text in languages, 2 decimal, 3 date-time, 4 date, 5 photo (a link),
/// 6 one of the property's choices, 7 several of them, 8 a place {lat, lng}, 9 an element (by id), 10 several texts in languages, 11 the
/// chosen choices of an activating list (one or several, kept as a list). The value is JSON of its kind, so it is checked once, when it is saved.
/// </summary>
public static class PropertyValues
{
    /// <summary>The value made canonical for its kind, or the reason it is refused.</summary>
    public static (JsonNode? Value, string? Error) Check(TypeInfo property, JsonNode? value, IReadOnlySet<Guid> choices)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(choices);
        if (value is null || (value is JsonValue v && v.GetValueKind() == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetValue<string>())))
        {
            return property.Required ? (null, "is required") : (null, null);
        }

        try
        {
            return property.Kind switch
            {
                PropertyKind.Text => Text(value),
                PropertyKind.Number => (JsonValue.Create(Number(value)), null),
                PropertyKind.DateTime => (JsonValue.Create(DateTimeOffset.Parse(value.GetValue<string>(), CultureInfo.InvariantCulture).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)), null),
                PropertyKind.Date => (JsonValue.Create(DateOnly.ParseExact(value.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), null),
                PropertyKind.Photo => (JsonValue.Create(value.GetValue<string>().Trim()), null),
                PropertyKind.List => Choice(value, choices),
                PropertyKind.MultiList => Choices(value, choices),
                PropertyKind.Location => Location(value),
                PropertyKind.Lookup => (JsonValue.Create(Guid.Parse(value.GetValue<string>()).ToString()), null),
                PropertyKind.MultiText => Texts(value),
                PropertyKind.ActivatingList => Choices(value is JsonValue ? new JsonArray(value.DeepClone()) : value, choices),
                _ => (null, "has an unknown kind"),
            };
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or JsonException or OverflowException or ArgumentException)
        {
            return (null, $"is not a valid {property.Kind}");
        }
    }

    private static (JsonNode?, string?) Text(JsonNode value)
    {
        // A text is one value per language; a plain string is taken as English.
        if (value is JsonValue)
        {
            return (new JsonObject { [TranslatedText.DefaultLanguage] = value.GetValue<string>().Trim() }, null);
        }

        var texts = value.AsObject().Where(p => p.Value is not null && !string.IsNullOrWhiteSpace(p.Value.GetValue<string>())).ToList();
        return texts.Count == 0 ? (null, "needs a text") : (new JsonObject(texts.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(p.Value!.GetValue<string>().Trim())))), null);
    }

    private static (JsonNode?, string?) Texts(JsonNode value)
    {
        // Each item is a text in languages (or a plain string, taken as English); empty items are dropped.
        var texts = value.AsArray().Where(n => n is not null && !(n is JsonValue v && string.IsNullOrWhiteSpace(v.GetValue<string>()))).Select(n => Text(n!)).Where(t => t.Item1 is not null).Select(t => t.Item1).ToList();
        return texts.Count == 0 ? (null, "needs a text") : (new JsonArray([.. texts]), null);
    }

    private static decimal Number(JsonNode value) =>
        value.GetValueKind() == JsonValueKind.Number ? decimal.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture) : decimal.Parse(value.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static (JsonNode?, string?) Choice(JsonNode value, IReadOnlySet<Guid> choices)
    {
        var id = Guid.Parse(value.GetValue<string>());
        return choices.Contains(id) ? (JsonValue.Create(id.ToString()), null) : (null, "is not one of its choices");
    }

    private static (JsonNode?, string?) Choices(JsonNode value, IReadOnlySet<Guid> choices)
    {
        var ids = value.AsArray().Select(n => Guid.Parse(n!.GetValue<string>())).Distinct().ToList();
        return ids.All(choices.Contains) ? (new JsonArray([.. ids.Select(i => (JsonNode?)JsonValue.Create(i.ToString()))]), null) : (null, "has a value that is not one of its choices");
    }

    private static (JsonNode?, string?) Location(JsonNode value)
    {
        var lat = Number(value["lat"]!);
        var lng = Number(value["lng"]!);
        return lat is < -90 or > 90 || lng is < -180 or > 180 ? (null, "is not a place on the map") : (new JsonObject { ["lat"] = lat, ["lng"] = lng }, null);
    }
}

using System.Text.Json.Nodes;

namespace Gamma.Erp.Models.Base;

/// <summary>
/// The value of one property for one element, e.g. BRAND = CISCO on a product. One row per element and property;
/// the value is JSON of the property's <see cref="PropertyKind"/>:
/// <list type="bullet">
/// <item>Text: <c>{"en": "Cable", "sq": "Kabull"}</c>, every language</item>
/// <item>Number: <c>1500.50</c></item>
/// <item>DateTime: <c>"2026-10-29T14:30:00+01:00"</c>; Date: <c>"2026-10-29"</c></item>
/// <item>Photo: <c>"files/…/photo.jpg"</c>, the link to the stored file</item>
/// <item>List: <c>"&lt;choice id&gt;"</c>; MultiList: <c>["&lt;id&gt;", "&lt;id&gt;"]</c>, so a renamed choice keeps its values</item>
/// <item>Location: <c>{"lat": 41.3275, "lng": 19.8187}</c></item>
/// <item>Lookup: <c>"&lt;element id&gt;"</c>, an element of the entity the property names (a weak link, like from and to)</item>
/// </list>
/// </summary>
public abstract class ElementInfo : Row
{
    public Guid ElementId { get; set; }

    public Guid TypeInfoId { get; set; }

    /// <summary>The value as JSON of the property's kind.</summary>
    public JsonNode? Value { get; set; }

    /// <summary>A second text with the value.</summary>
    public TranslatedText? ValueDescription { get; set; }
}

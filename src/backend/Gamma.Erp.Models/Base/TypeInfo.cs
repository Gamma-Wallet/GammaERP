namespace Gamma.Erp.Models.Base;

/// <summary>
/// What a dynamic property of a type holds, and so how its value (JSON in <see cref="ElementInfo.Value"/>) is read (founder, 2026-10-29).
/// </summary>
public enum PropertyKind
{
    /// <summary>A text in every language: <c>{"en": "…", "sq": "…"}</c>.</summary>
    Text = 1,

    /// <summary>An exact decimal: <c>1500.50</c>.</summary>
    Number = 2,

    /// <summary><c>"2026-10-29T14:30:00+01:00"</c>.</summary>
    DateTime = 3,

    /// <summary><c>"2026-10-29"</c>.</summary>
    Date = 4,

    /// <summary>The link to the stored photo.</summary>
    Photo = 5,

    /// <summary>One of the property's choices (<see cref="TypeInfoChoice"/>), by id.</summary>
    List = 6,

    /// <summary>Several of the property's choices, by id.</summary>
    MultiList = 7,

    /// <summary>A point shown on a map: <c>{"lat": …, "lng": …}</c>.</summary>
    Location = 8,

    /// <summary>An element of the entity named by <see cref="TypeInfo.ElementsId"/>, by id.</summary>
    Lookup = 9,

    /// <summary>Several texts, each in every language: <c>[{"en": "…"}, {"en": "…"}]</c>.</summary>
    MultiText = 10,

    /// <summary>
    /// Choices that show more properties: one or several of its choices, by id; a property whose
    /// <see cref="TypeInfo.ShownForChoiceId"/> is a chosen one is shown and checked, the others are not.
    /// </summary>
    ActivatingList = 11,
}

/// <summary>A dynamic property of a type, e.g. BRAND or MODEL on a product type; its values are in the element info.</summary>
public abstract class TypeInfo : Row
{
    public Guid TypeId { get; set; }

    public TranslatedText Name { get; set; } = new();

    /// <summary>
    /// A fixed key the engine finds the property by (e.g. BARCODE, PHONE, TAX_NUMBER, CITY, NEGATIVE_STOCK), so its name can be renamed and translated freely.
    /// Empty for a property the engine does not read.
    /// </summary>
    public string? Code { get; set; }

    public TranslatedText? Description { get; set; }

    /// <summary>Must be filled in.</summary>
    public bool Required { get; set; }

    /// <summary>What the property holds, and so which value column is filled.</summary>
    public PropertyKind Kind { get; set; }

    /// <summary>Order on the form.</summary>
    public double SortOrder { get; set; }

    /// <summary>For a lookup: the entity (scope) it picks from.</summary>
    public Guid? ElementsId { get; set; }

    /// <summary>
    /// For a lookup: the element a new element of the type gets when none is chosen (new), e.g. the cash account a new cash desk holds,
    /// proposed by the country's pack. A soft link: when that element is not loaded or copied, the property stays and the default is empty.
    /// </summary>
    public Guid? DefaultElementId { get; set; }

    /// <summary>
    /// The choice of an activating list of the same type that shows this property: only when that choice is picked is
    /// the property filled in and, when <see cref="Required"/>, required. Empty: always shown.
    /// </summary>
    public Guid? ShownForChoiceId { get; set; }

    /// <summary>The value is a file.</summary>
    public bool IsFile { get; set; }
}

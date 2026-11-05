namespace Gamma.Erp.Models.Base;

/// <summary>
/// A category of an entity, arranged as a tree through <see cref="Row.IdSup"/>: a grouping or an element of the tree
/// (<see cref="IsElement"/>). Types and elements are put in categories through the inter tables.
/// </summary>
public abstract class Category : Row
{
    /// <summary>In the tree: true = an element, false = a grouping.</summary>
    public bool IsElement { get; set; }

    public string? Code { get; set; }

    public TranslatedText Name { get; set; } = new();

    public TranslatedText? Description { get; set; }

    /// <summary>Order among its siblings.</summary>
    public double SortOrder { get; set; }
}

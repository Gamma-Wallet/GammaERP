namespace Gamma.Erp.Models.Base;

/// <summary>
/// A type of an entity, arranged as a tree through <see cref="Row.IdSup"/>: an element's type must be a child of its
/// parent element's type. A type numbers its elements between <see cref="CodeStart"/> and <see cref="CodeEnd"/>, and defines the
/// dynamic properties of its elements (<see cref="TypeInfo"/>).
/// </summary>
public abstract class ElementType : Row
{
    /// <summary>In the tree: true = an element, false = a grouping.</summary>
    public bool IsElement { get; set; }

    public string? Code { get; set; }

    /// <summary>The first number of the element codes of this type.</summary>
    public int? CodeStart { get; set; }

    /// <summary>The last number.</summary>
    public int? CodeEnd { get; set; }

    /// <summary>The number the next element takes.</summary>
    public int? CodeCurrent { get; set; }

    public TranslatedText Name { get; set; } = new();

    public TranslatedText? Description { get; set; }

    /// <summary>Order among its siblings.</summary>
    public double SortOrder { get; set; }
}

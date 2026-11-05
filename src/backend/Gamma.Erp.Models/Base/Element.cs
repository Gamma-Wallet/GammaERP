namespace Gamma.Erp.Models.Base;

/// <summary>
/// An element of an entity: a partner, a product, a cost centre… Its parent element is <see cref="Row.IdSup"/>; everything it
/// has beyond these columns is a value of its type's properties (<see cref="ElementInfo"/>), and it is put in categories through the
/// inter element-category table.
/// </summary>
public abstract class Element : Row
{
    public Guid TypeId { get; set; }

    public string? Code { get; set; }

    public TranslatedText Name { get; set; } = new();

    public TranslatedText? Description { get; set; }

    /// <summary>The element is made of other elements.</summary>
    public bool IsComposite { get; set; }
}

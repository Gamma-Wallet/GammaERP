namespace Gamma.Erp.Models.Base;

/// <summary>An element in a category, many to many: a partner in Clients and in Suppliers.</summary>
public abstract class InterElementCategory : Row
{
    public Guid ElementId { get; set; }

    public Guid CategoryId { get; set; }
}

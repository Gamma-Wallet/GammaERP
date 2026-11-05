namespace Gamma.Erp.Models.Base;

/// <summary>A type in a category, many to many: the partner type SME is in Clients and in Suppliers.</summary>
public abstract class InterTypeCategory : Row
{
    public Guid TypeId { get; set; }

    public Guid CategoryId { get; set; }
}

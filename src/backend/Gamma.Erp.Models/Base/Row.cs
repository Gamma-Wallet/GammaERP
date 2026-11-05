namespace Gamma.Erp.Models.Base;

/// <summary>
/// The standard columns every table has.
/// Nothing is ever deleted: a deleted row has <see cref="Active"/> false. A change first copies the row as it was into a new, disabled
/// row of the same table, whose id goes into <see cref="IdChange"/>; the row keeps its id, so everything pointing at it stays.
/// </summary>
public abstract class Row
{
    /// <summary>The row: a GUID given when the row is made, so several new rows can be added before saving.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The corporate the row belongs to; the database isolates each corporate.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The cost centre the row belongs to: the corporate (the root), a company, or one of its cost centres. A node sees only its own rows
    /// and those of the nodes below it, never above: Ristorante Roma sees Roma, Kitchen and Bar, not the corporate. A company's
    /// configuration (document types, VAT…) belongs to the company and applies to every cost centre inside it. Empty only on the
    /// organisation's own definitions (what a corporate and a company are), which every node reads.
    /// </summary>
    public Guid? OwnerId { get; set; }

    /// <summary>The parent row of the same table, for trees; null at the top.</summary>
    public Guid? IdSup { get; set; }

    /// <summary>The disabled copy holding this row as it was before its last change; null when it never changed.</summary>
    public Guid? IdChange { get; set; }

    /// <summary>False when deleted (soft) or a history copy.</summary>
    public bool Active { get; set; } = true;

    /// <summary>When this version of the row was written.</summary>
    public DateTimeOffset CreatedOn { get; set; }

    /// <summary>Who wrote this version of the row.</summary>
    public Guid? CreatedBy { get; set; }
}

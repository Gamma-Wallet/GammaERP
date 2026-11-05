using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A type of financial document.</summary>
[Table("tbl_document_type")]
public sealed class DocumentType : ElementType
{
    public Guid? MenuId { get; set; }

    /// <summary>
    /// Its documents are never posted (the founder, 2026-10-30): offers and contracts are kept as documents but make no entries; posting one
    /// is refused.
    /// </summary>
    public bool NeverPosted { get; set; }

    /// <summary>Whether a reversal of this type's documents may take only some of the lines, or less than their quantity.</summary>
    public bool AllowsPartialReversal { get; set; }

    /// <summary>
    /// A reversal type (reversal of a sales invoice, of a receipt, of a journal…; the founder's rules, 2026-10-30): it reverses exactly one type
    /// (<see cref="ReversedDocumentTypeId"/>), and its documents are made only by reversing a posted document of that type, each one
    /// carrying the document it reverses. A posted document is never unposted or changed: it is corrected this way.
    /// </summary>
    public bool IsReversal { get; set; }

    /// <summary>
    /// For a reversal type, the type it reverses (e.g. sales invoice reversal → sales invoice): set exactly when <see cref="IsReversal"/>, and
    /// no two reversal types reverse the same type. A type no reversal type reverses can't be reversed until one is set.
    /// </summary>
    public Guid? ReversedDocumentTypeId { get; set; }

    /// <summary>Its documents stay open until payments cover them (invoices).</summary>
    public bool HoldsOpenItem { get; set; }

    /// <summary>Its documents pay open ones: a line names the code of the document it covers.</summary>
    public bool PaysOpenItems { get; set; }

    /// <summary>Its documents are contracts: they can have a billing plan, and show on the renewals.</summary>
    public bool BillsByPlan { get; set; }

    /// <summary>Its documents over their writer's limit wait for approval before they become the next document.</summary>
    public bool NeedsApproval { get; set; }

    /// <summary>Its posted documents sell the fixed assets on their lines. A transfer to a client does not: the asset stays the company's (the founder, 2026-11-02).</summary>
    public bool SellsAssets { get; set; }

    /// <summary>Its lines go to the production point display.</summary>
    public bool SendsToProduction { get; set; }

    /// <summary>The production document made when a production point says a line of this type was produced.</summary>
    public Guid? ProductionDocumentTypeId { get; set; }

    /// <summary>Its orders are listed on the Production screen, to be produced from their recipes.</summary>
    public bool ProducedOnProductionScreen { get; set; }

    /// <summary>A line of it that comes from a produced order line takes the product itself, not its recipe's ingredients.</summary>
    public bool SellsProducedProduct { get; set; }

    /// <summary>A new job on the repair board opens a document of this type.</summary>
    public bool StartsRepair { get; set; }

    /// <summary>A person can mark its documents accepted or refused by the client.</summary>
    public bool CanBeAccepted { get; set; }
}

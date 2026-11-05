using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A financial document.</summary>
[Table("tbl_document")]
public sealed class Document : Element
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public DateOnly? PostingDate { get; set; }

    public DateOnly? DocumentDate { get; set; }

    /// <summary>Where it comes from: a weak link whose entity the document type's configuration says.</summary>
    public Guid? FromId { get; set; }

    /// <summary>The name it had when the row was made.</summary>
    public string? FromName { get; set; }

    /// <summary>Where it goes to: a weak link whose entity the document type's configuration says.</summary>
    public Guid? ToId { get; set; }

    /// <summary>The name it had when the row was made.</summary>
    public string? ToName { get; set; }

    public bool Posted { get; set; }

    /// <summary>
    /// A number the user types, e.g. the paper or supplier's invoice number, next to the document's own code.
    /// </summary>
    public string? ExternalNumber { get; set; }

    /// <summary>When an invoice falls due; left empty, posting gives it the date plus the partner's payment days.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>The opportunity an offer belongs to.</summary>
    public Guid? OpportunityId { get; set; }

    /// <summary>Who approved it; changing the document clears it.</summary>
    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedOn { get; set; }

    /// <summary>Who marked it accepted by the client; changing the document clears it.</summary>
    public Guid? AcceptedBy { get; set; }

    public DateTimeOffset? AcceptedOn { get; set; }

    public Guid? RefusedBy { get; set; }

    public DateTimeOffset? RefusedOn { get; set; }

    /// <summary>How the document is paid (cash, card…; a payment method the document type allows), e.g. a point-of-sale bill.</summary>
    public Guid? PaymentMethodId { get; set; }

    /// <summary>The table a point-of-sale bill is for (a cost centre of the Table type), so a table's bills are found and paid together.</summary>
    public Guid? TableId { get; set; }

    /// <summary>
    /// For a document of a reversal type: the document it reverses, always (the founder's rule, 2026-10-30). Set when the reversal is made from it, never changed.
    /// </summary>
    public Guid? ReversedDocumentId { get; set; }

    /// <summary>The reversed document's number, kept on the reversal as it was made (the reversal also has its own number, of its own series).</summary>
    public string? ReversedDocumentCode { get; set; }

    /// <summary>The group's sale this purchase was made from: written when it is made, never changed. The company can't see the sale; its number is the external number.</summary>
    public Guid? GroupSaleId { get; set; }
}

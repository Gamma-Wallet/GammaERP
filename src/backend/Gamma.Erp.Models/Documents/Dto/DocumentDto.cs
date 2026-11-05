using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Transactions.Dto;

namespace Gamma.Erp.Models.Documents.Dto;

/// <summary>A financial document, with its categories and property values, as the screens see it.</summary>
public sealed record DocumentDto : ElementDto
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; init; }

    public DateOnly? PostingDate { get; init; }

    public DateOnly? DocumentDate { get; init; }

    /// <summary>Where it comes from: a weak link whose entity the document type's configuration says.</summary>
    public Guid? FromId { get; init; }

    /// <summary>The name it had when the row was made.</summary>
    public string? FromName { get; init; }

    /// <summary>Where it goes to: a weak link whose entity the document type's configuration says.</summary>
    public Guid? ToId { get; init; }

    /// <summary>The name it had when the row was made.</summary>
    public string? ToName { get; init; }

    public bool Posted { get; init; }

    /// <summary>A number the user types (the paper or supplier's invoice number), next to the document's own code.</summary>
    public string? ExternalNumber { get; init; }

    public DateOnly? DueDate { get; init; }

    public Guid? OpportunityId { get; init; }

    /// <summary>How the document is paid (a payment method the type allows).</summary>
    public Guid? PaymentMethodId { get; init; }

    /// <summary>The table a point-of-sale bill is for.</summary>
    public Guid? TableId { get; init; }

    /// <summary>Its transactions, in order.</summary>
    public IReadOnlyList<TransactionDto> Transactions { get; init; } = [];

    /// <summary>The documents it was created from (tbl_inter_document_document), e.g. the order an invoice came from.</summary>
    public IReadOnlyList<Guid> CreatedFromIds { get; init; } = [];

    /// <summary>Its totals, one per currency, worked out from the transactions (read only).</summary>
    public IReadOnlyList<DocumentTotalDto> Totals { get; init; } = [];

    /// <summary>For a reversal: the document it reverses and that document's number (read only: set when the reversal is made from it).</summary>
    public Guid? ReversedDocumentId { get; init; }

    public string? ReversedDocumentCode { get; init; }
}

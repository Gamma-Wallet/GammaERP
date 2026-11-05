using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Documents.Dto;

/// <summary>A type of financial document, with its categories and properties, as the screens see it.</summary>
public sealed record DocumentTypeDto : ElementTypeDto
{
    /// <summary>Its documents are never posted (offers, contracts).</summary>
    public bool NeverPosted { get; init; }

    /// <summary>Whether a reversal may take only some of the lines or quantities.</summary>
    public bool AllowsPartialReversal { get; init; }

    /// <summary>A reversal type: its documents are made only by reversing a document of the type it reverses.</summary>
    public bool IsReversal { get; init; }

    /// <summary>For a reversal type, the type it reverses.</summary>
    public Guid? ReversedDocumentTypeId { get; init; }
}

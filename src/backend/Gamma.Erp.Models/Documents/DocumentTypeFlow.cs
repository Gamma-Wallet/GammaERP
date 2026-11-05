using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>
/// Which document type a document may make: offer to contract, offer to
/// invoice, contract to invoice. The new document starts unposted with the source's lines.
/// </summary>
[Table("tbl_document_type_flow")]
public sealed class DocumentTypeFlow : Row
{
    public Guid SourceTypeId { get; set; }

    public Guid TargetTypeId { get; set; }

    /// <summary>A document makes only one of the target type: an offer is taken up once; a contract is billed again and again.</summary>
    public bool Once { get; set; }

    /// <summary>The source must be marked accepted first.</summary>
    public bool NeedsAcceptance { get; set; }

    /// <summary>The source's date property after which it makes nothing more, e.g. a contract's end date; empty: no limit.</summary>
    public Guid? ValidUntilInfoId { get; set; }
}

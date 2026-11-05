using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A document created from another: document2 was made from document1.</summary>
[Table("tbl_inter_document_document")]
public sealed class InterDocumentDocument : Row
{
    public Guid? Document1Id { get; set; }

    public Guid? Document2Id { get; set; }
}

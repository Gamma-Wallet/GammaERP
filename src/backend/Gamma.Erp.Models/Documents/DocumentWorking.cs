using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A document held by the user editing it: one held document per user; others may read it but not change it.</summary>
[Table("tbl_document_working")]
public sealed class DocumentWorking : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid? DocumentId { get; set; }

    /// <summary>The user holding it.</summary>
    public Guid? UserId { get; set; }

    public bool Working { get; set; }
}

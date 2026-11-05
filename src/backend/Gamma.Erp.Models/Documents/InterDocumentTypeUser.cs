using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>The document types a user, or a role, may use.</summary>
[Table("tbl_inter_document_type_user")]
public sealed class InterDocumentTypeUser : Row
{
    public Guid? DocumentTypeId { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>The role (a user category); null when the row is for a user.</summary>
    public Guid? RoleId { get; set; }
}

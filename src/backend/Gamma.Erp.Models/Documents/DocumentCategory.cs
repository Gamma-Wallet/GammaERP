using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A category of financial documents.</summary>
[Table("tbl_document_category")]
public sealed class DocumentCategory : Category;

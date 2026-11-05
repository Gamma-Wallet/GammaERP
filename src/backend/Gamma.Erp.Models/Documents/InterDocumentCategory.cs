using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A financial document in a financial document category.</summary>
[Table("tbl_inter_document_category")]
public sealed class InterDocumentCategory : InterElementCategory;

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A choice of a financial document list property.</summary>
[Table("tbl_document_type_info_ch")]
public sealed class DocumentTypeInfoChoice : TypeInfoChoice;

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A property of a financial document type.</summary>
[Table("tbl_document_type_info")]
public sealed class DocumentTypeInfo : TypeInfo;

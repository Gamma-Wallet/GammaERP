using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>The value of a property for a financial document.</summary>
[Table("tbl_document_info")]
public sealed class DocumentInfo : ElementInfo;

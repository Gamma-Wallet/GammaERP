using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A user who may see a financial document.</summary>
[Table("tbl_document_access")]
public sealed class DocumentAccess : ElementAccess;

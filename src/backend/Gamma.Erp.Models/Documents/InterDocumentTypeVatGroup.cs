using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

[Table("tbl_inter_document_type_vat_group")]
public sealed class InterDocumentTypeVatGroup : Row
{
    public Guid? DocumentTypeId { get; set; }

    public Guid? VatGroupId { get; set; }
}

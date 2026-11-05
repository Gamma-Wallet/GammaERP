using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

[Table("tbl_inter_payment_method_document_type")]
public sealed class InterPaymentMethodDocumentType : Row
{
    public Guid? PaymentMethodId { get; set; }

    public Guid? DocumentTypeId { get; set; }
}

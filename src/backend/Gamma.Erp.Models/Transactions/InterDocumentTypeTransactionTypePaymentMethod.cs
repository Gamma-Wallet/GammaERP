using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

[Table("tbl_inter_document_type_transaction_type_payment_method")]
public sealed class InterDocumentTypeTransactionTypePaymentMethod : Row
{
    public Guid? PaymentMethodDocumentTypeId { get; set; }

    public Guid? TransactionTypeId { get; set; }
}

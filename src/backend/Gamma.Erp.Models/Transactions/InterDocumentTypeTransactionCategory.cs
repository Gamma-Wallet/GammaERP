using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

[Table("tbl_inter_document_type_transaction_category")]
public sealed class InterDocumentTypeTransactionCategory : Row
{
    public Guid? DocumentTypeId { get; set; }

    public Guid? TransactionCategoryId { get; set; }
}

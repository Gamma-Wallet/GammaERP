using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

[Table("tbl_inter_document_type_transaction_type")]
public sealed class InterDocumentTypeTransactionType : Row
{
    public Guid? DocumentTypeId { get; set; }

    public Guid? TransactionTypeId { get; set; }

    /// <summary>The amount of a line that goes on the debit of its element's own balance (a journal row on a client, cash desk or staff); empty: none.</summary>
    public Guid? ElementDebitValueElementsId { get; set; }

    /// <summary>The amount that goes on the credit of the element's own balance.</summary>
    public Guid? ElementCreditValueElementsId { get; set; }
}

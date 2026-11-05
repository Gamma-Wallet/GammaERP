using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

[Table("tbl_transaction_type_conf_to")]
public sealed class TransactionTypeConfTo : Row
{
    public Guid? DocumentTypeTransactionTypeId { get; set; }

    public Guid? ElementsId { get; set; }
}

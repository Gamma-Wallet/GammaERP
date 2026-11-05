using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

[Table("tbl_transaction_type_conf_from")]
public sealed class TransactionTypeConfFrom : Row
{
    public Guid? DocumentTypeTransactionTypeId { get; set; }

    public Guid? ElementsId { get; set; }
}

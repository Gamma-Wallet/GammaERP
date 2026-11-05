using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

[Table("tbl_transaction_type_conf")]
public sealed class TransactionTypeConf : Row
{
    public Guid? TransactionTypeId { get; set; }

    public Guid? ElementsId { get; set; }

    /// <summary>
    /// Whether the lines of this configuration move stock: 1 = the from side's stock goes down and the to side's up by the quantity, 0 = no
    /// stock (services, accounts…).
    /// </summary>
    public int StockDirection { get; set; }
}

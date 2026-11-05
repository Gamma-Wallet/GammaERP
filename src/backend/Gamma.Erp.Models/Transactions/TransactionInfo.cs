using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

/// <summary>The value of a property for a transaction.</summary>
[Table("tbl_transaction_info")]
public sealed class TransactionInfo : ElementInfo;

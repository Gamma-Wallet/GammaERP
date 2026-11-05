using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

/// <summary>A choice of a transaction list property.</summary>
[Table("tbl_transaction_type_info_ch")]
public sealed class TransactionTypeInfoChoice : TypeInfoChoice;

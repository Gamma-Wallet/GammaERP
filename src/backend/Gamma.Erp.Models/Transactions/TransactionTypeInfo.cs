using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

/// <summary>A property of a transaction type.</summary>
[Table("tbl_transaction_type_info")]
public sealed class TransactionTypeInfo : TypeInfo;

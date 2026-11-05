using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

/// <summary>A transaction type in a transaction category.</summary>
[Table("tbl_inter_transaction_type_category")]
public sealed class InterTransactionTypeCategory : InterTypeCategory;

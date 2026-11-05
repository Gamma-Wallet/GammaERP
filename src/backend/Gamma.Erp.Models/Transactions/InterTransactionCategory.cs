using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

/// <summary>A transaction in a transaction category.</summary>
[Table("tbl_inter_transaction_category")]
public sealed class InterTransactionCategory : InterElementCategory;

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

/// <summary>A category of transactions.</summary>
[Table("tbl_transaction_category")]
public sealed class TransactionCategory : Category;

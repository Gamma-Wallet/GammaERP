using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

/// <summary>A type of transaction.</summary>
[Table("tbl_transaction_type")]
public sealed class TransactionType : ElementType;

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Transactions;

/// <summary>A user who may see a transaction.</summary>
[Table("tbl_transaction_access")]
public sealed class TransactionAccess : ElementAccess;

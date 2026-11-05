using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.CostCenters;

[Table("tbl_inter_payment_method_cost_center")]
public sealed class InterPaymentMethodCostCenter : Row
{
    public Guid? PaymentMethodId { get; set; }

    public Guid? CostCenterId { get; set; }

    /// <summary>
    /// The cash desk or bank this method's money goes to at this cost centre (card payments at a shop to the bank account, say); empty: where
    /// the point of sale's settings send it (the founder, 2026-11-04).
    /// </summary>
    public Guid? CashBankId { get; set; }
}

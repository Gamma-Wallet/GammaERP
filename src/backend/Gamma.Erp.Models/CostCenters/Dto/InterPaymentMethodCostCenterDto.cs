using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.CostCenters.Dto;

public sealed record InterPaymentMethodCostCenterDto : RowDto
{
    public Guid? PaymentMethodId { get; init; }

    public Guid? CostCenterId { get; init; }

    public bool CashBank { get; init; }
}

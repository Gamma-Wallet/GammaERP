using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.CostCenters.Dto;

public sealed record InterUserCostCenterDto : RowDto
{
    public Guid? UserId { get; init; }

    public Guid? CostCenterId { get; init; }
}

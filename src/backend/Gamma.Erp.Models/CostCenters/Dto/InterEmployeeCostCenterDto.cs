using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.CostCenters.Dto;

public sealed record InterEmployeeCostCenterDto : RowDto
{
    public Guid? EmployeeId { get; init; }

    public Guid? CostCenterId { get; init; }
}

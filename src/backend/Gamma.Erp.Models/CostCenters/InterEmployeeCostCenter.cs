using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.CostCenters;

[Table("tbl_inter_employee_cost_center")]
public sealed class InterEmployeeCostCenter : Row
{
    public Guid? EmployeeId { get; set; }

    public Guid? CostCenterId { get; set; }
}

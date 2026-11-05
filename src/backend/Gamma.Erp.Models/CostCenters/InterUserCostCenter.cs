using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.CostCenters;

[Table("tbl_inter_user_cost_center")]
public sealed class InterUserCostCenter : Row
{
    public Guid? UserId { get; set; }

    public Guid? CostCenterId { get; set; }
}

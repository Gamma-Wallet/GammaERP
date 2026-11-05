using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.CostCenters;

/// <summary>A user who may see a cost centre.</summary>
[Table("tbl_cost_center_access")]
public sealed class CostCenterAccess : ElementAccess;

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.CostCenters;

/// <summary>The value of a property for a cost centre.</summary>
[Table("tbl_cost_center_info")]
public sealed class CostCenterInfo : ElementInfo;

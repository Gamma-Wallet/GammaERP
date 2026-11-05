using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.CostCenters;

/// <summary>A cost centre in a cost centre category.</summary>
[Table("tbl_inter_cost_center_category")]
public sealed class InterCostCenterCategory : InterElementCategory;

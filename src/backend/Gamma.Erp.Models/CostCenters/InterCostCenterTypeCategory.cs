using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.CostCenters;

/// <summary>A cost centre type in a cost centre category.</summary>
[Table("tbl_inter_cost_center_type_category")]
public sealed class InterCostCenterTypeCategory : InterTypeCategory;

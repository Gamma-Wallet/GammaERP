using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.CostCenters;

/// <summary>A category of cost centres.</summary>
[Table("tbl_cost_center_category")]
public sealed class CostCenterCategory : Category;

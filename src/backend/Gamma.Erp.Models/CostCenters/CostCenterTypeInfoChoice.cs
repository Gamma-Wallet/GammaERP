using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.CostCenters;

/// <summary>A choice of a cost centre list property.</summary>
[Table("tbl_cost_center_type_info_ch")]
public sealed class CostCenterTypeInfoChoice : TypeInfoChoice;

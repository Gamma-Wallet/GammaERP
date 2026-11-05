using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.CostCenters;

/// <summary>A property of a cost centre type.</summary>
[Table("tbl_cost_center_type_info")]
public sealed class CostCenterTypeInfo : TypeInfo;

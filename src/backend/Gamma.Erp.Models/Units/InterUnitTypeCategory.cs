using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Units;

/// <summary>A unit of measure type in a unit of measure category.</summary>
[Table("tbl_inter_unit_type_category")]
public sealed class InterUnitTypeCategory : InterTypeCategory;

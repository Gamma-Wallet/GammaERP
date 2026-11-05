using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Units;

/// <summary>A choice of a unit of measure list property.</summary>
[Table("tbl_unit_type_info_ch")]
public sealed class UnitTypeInfoChoice : TypeInfoChoice;

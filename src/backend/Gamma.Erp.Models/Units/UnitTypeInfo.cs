using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Units;

/// <summary>A property of a unit of measure type.</summary>
[Table("tbl_unit_type_info")]
public sealed class UnitTypeInfo : TypeInfo;

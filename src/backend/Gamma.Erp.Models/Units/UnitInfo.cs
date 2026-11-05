using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Units;

/// <summary>The value of a property for a unit of measure.</summary>
[Table("tbl_unit_info")]
public sealed class UnitInfo : ElementInfo;

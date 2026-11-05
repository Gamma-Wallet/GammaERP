using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Units;

/// <summary>A type of unit of measure.</summary>
[Table("tbl_unit_type")]
public sealed class UnitType : ElementType;

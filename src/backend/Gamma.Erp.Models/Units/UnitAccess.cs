using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Units;

/// <summary>A user who may see a unit of measure.</summary>
[Table("tbl_unit_access")]
public sealed class UnitAccess : ElementAccess;

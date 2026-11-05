using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Units;

/// <summary>A category of unit of measures.</summary>
[Table("tbl_unit_category")]
public sealed class UnitCategory : Category;

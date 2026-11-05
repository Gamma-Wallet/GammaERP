using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Units;

/// <summary>A unit of measure.</summary>
[Table("tbl_unit")]
public sealed class Unit : Element;

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Units;

/// <summary>
/// How many of one unit make one of another: 1 kg = 1000 g, 1 bottle = 0.7 l. For every product,
/// or for one product (a bottle of this wine), which then wins; it also works the other way round (1 g = 0.001 kg).
/// </summary>
[Table("tbl_unit_conversion")]
public sealed class UnitConversion : Row
{
    public Guid FromUnitId { get; set; }

    public Guid ToUnitId { get; set; }

    /// <summary>How many <see cref="ToUnitId"/> one <see cref="FromUnitId"/> is; above 0.</summary>
    public decimal Factor { get; set; }

    /// <summary>The product it is for (empty: every product).</summary>
    public Guid? ProductId { get; set; }
}

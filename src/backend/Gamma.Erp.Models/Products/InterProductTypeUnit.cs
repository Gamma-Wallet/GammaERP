using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>
/// A unit a product type is sold and bought in: what a product of the type
/// is offered when it has no units of its own. The first by <see cref="SortOrder"/> is the default.
/// </summary>
[Table("tbl_inter_product_type_unit")]
public sealed class InterProductTypeUnit : Row
{
    public Guid? ProductTypeId { get; set; }

    public Guid? UnitId { get; set; }

    /// <summary>The unit's place in the type's list; the first is the default (the founder, 2026-10-31).</summary>
    public double SortOrder { get; set; }
}

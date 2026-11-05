using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>
/// A unit one product is sold, bought and kept in: its stock, prices, cost layers and assets point
/// at this row. Set in the product's Units tab, or made at the product's first movement in a unit. The first by <see cref="SortOrder"/> is the
/// default a document line proposes.
/// </summary>
[Table("tbl_inter_product_specific_unit")]
public sealed class InterProductSpecificUnit : Row
{
    public Guid? ProductId { get; set; }

    public Guid? UnitId { get; set; }

    /// <summary>The unit's place in the product's list; the first is the default (the founder, 2026-10-31).</summary>
    public double SortOrder { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>
/// What one document line took from (or gave back to) a cost layer: how much and at what value. A reversal of a sale puts back exactly what
/// the sale took (a negative row), a reversal of a purchase takes its own layer off, and a layer below zero settled by stock coming in
/// records the difference between the cost it was valued at and the real one.
/// </summary>
[Table("tbl_product_cost_layer_use")]
public sealed class ProductCostLayerUse : Row
{
    public Guid CompanyId { get; set; }

    public Guid LayerId { get; set; }

    public Guid? DocumentId { get; set; }

    public Guid? TransactionId { get; set; }

    /// <summary>The product the line moved for it (an ingredient of a composite product, or the line's own).</summary>
    public Guid ProductSpecificUnitId { get; set; }

    /// <summary>Taken (above 0) or given back (below 0).</summary>
    public decimal Quantity { get; set; }

    public decimal ValueLcu { get; set; }

    /// <summary>What it was: 1 taken, 2 put back by a reversal, 3 taken off by a reversal of the purchase, 4 a layer below zero settled.</summary>
    public int Kind { get; set; }
}

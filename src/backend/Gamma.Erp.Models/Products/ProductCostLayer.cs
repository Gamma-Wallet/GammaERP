using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>
/// A quantity of a product (in a unit) a place holds at one cost, first in first out (the founder's rule, 2026-10-30): stock comes in only
/// through a purchase bill, which makes the layer; a transfer carries it to another place with its cost and date; a sale, a production or a
/// split takes from a place's oldest layers first. A place that gave away more than it held (where its company allows it) has a layer below
/// zero, valued at the oldest cost the company held elsewhere and settled by what comes in next. New.
/// </summary>
[Table("tbl_product_cost_layer")]
public sealed class ProductCostLayer : Row
{
    public Guid CompanyId { get; set; }

    /// <summary>The place holding it: a cost centre, an employee or a client keeping the company's goods.</summary>
    public Guid HolderId { get; set; }

    public Guid ProductSpecificUnitId { get; set; }

    /// <summary>The document and line that brought it to this place.</summary>
    public Guid? DocumentId { get; set; }

    public Guid? TransactionId { get; set; }

    /// <summary>The layer a transfer carried it from, so its purchase stays traceable.</summary>
    public Guid? OriginLayerId { get; set; }

    /// <summary>When it was bought (a carried layer keeps its purchase's date): the order layers are taken in.</summary>
    public DateOnly LayerDate { get; set; }

    public decimal QuantityIn { get; set; }

    public decimal QuantityLeft { get; set; }

    /// <summary>What is left of its value in the company's currency (all of it goes with the last of the quantity, so nothing is lost to rounding).</summary>
    public decimal ValueLeftLcu { get; set; }

    public decimal UnitCostLcu { get; set; }
}

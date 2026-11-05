using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>
/// One ingredient of a product made of others: how
/// much of which product, in which unit, goes into one of the product (in its unit, or in any), and the share lost on the way.
/// A product marked composite (<see cref="Element.IsComposite"/>) with ingredients gives them up from stock when it is sold, instead of itself.
/// </summary>
[Table("tbl_product_recipe")]
public sealed class ProductRecipe : Row
{
    public Guid ProductId { get; set; }

    /// <summary>The product's unit the quantities are for (empty: any).</summary>
    public Guid? UnitId { get; set; }

    public Guid ComponentId { get; set; }

    public Guid ComponentUnitId { get; set; }

    /// <summary>How much of the ingredient one of the product takes, before waste; above 0.</summary>
    public decimal Quantity { get; set; }

    /// <summary>The share of the ingredient lost in making it, in percent: 10 takes 1.1 times the quantity.</summary>
    public decimal WastePercent { get; set; }

    /// <summary>
    /// The share of the ingredient that comes back when the product is taken apart (a disassembly), in percent, 0 to 100; the rest is waste.
    /// </summary>
    public decimal RecoveredPercent { get; set; } = 100;

    public double SortOrder { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>
/// A sale menu of the point of sale: when it is open and where; the product
/// categories it shows are its <see cref="SaleMenuCategory"/> rows (the founder, 2026-10-30: a table of its own).
/// </summary>
[Table("tbl_sale_menu")]
public sealed class SaleMenu : Row
{
    public string? Code { get; set; }

    public TranslatedText Name { get; set; } = new();

    /// <summary>When it opens each day (empty: from midnight).</summary>
    public TimeOnly? StartsAt { get; set; }

    /// <summary>When it closes each day (empty: until midnight); before <see cref="StartsAt"/> it runs past midnight.</summary>
    public TimeOnly? EndsAt { get; set; }

    /// <summary>The point of sale it is for (empty: every point of sale of the company).</summary>
    public Guid? PointOfSaleId { get; set; }

    public double SortOrder { get; set; }
}

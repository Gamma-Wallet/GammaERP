using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>Which production point makes what: configuration. A row for one product goes before its category's, a point of sale's own before one for all.</summary>
[Table("tbl_production_route")]
public sealed class ProductionRoute : Row
{
    /// <summary>The point of sale it is for; empty: all.</summary>
    public Guid? PointOfSaleId { get; set; }

    public Guid? ProductCategoryId { get; set; }

    public Guid? ProductId { get; set; }

    /// <summary>The production point (a cost centre).</summary>
    public Guid ProductionPointId { get; set; }

    /// <summary>After how many minutes a card shows as late; the point uses the lowest of its rows.</summary>
    public int? WarningMinutes { get; set; }
}

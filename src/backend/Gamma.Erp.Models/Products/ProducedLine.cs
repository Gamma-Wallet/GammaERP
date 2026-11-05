using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>An order line a production point said was produced, by hand.</summary>
[Table("tbl_produced_line")]
public sealed class ProducedLine : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>The line of the order.</summary>
    public Guid BillLineId { get; set; }

    public Guid ProductionPointId { get; set; }

    /// <summary>The production document it made; empty for an item with no recipe, which is only marked.</summary>
    public Guid? ProductionDocumentId { get; set; }

    public Guid? ProducedBy { get; set; }

    public DateTimeOffset ProducedOn { get; set; }
}

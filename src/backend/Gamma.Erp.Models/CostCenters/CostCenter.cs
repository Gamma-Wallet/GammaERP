using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.CostCenters;

/// <summary>A cost centre.</summary>
[Table("tbl_cost_center")]
public sealed class CostCenter : Element
{
    /// <summary>On a company or a group: its own language, which with English are the two it works in (the founder, 2026-11-02). Empty below them.</summary>
    public string? Language { get; set; }
}

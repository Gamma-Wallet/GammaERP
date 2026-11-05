using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.CostCenters;

/// <summary>A type of cost centre.</summary>
[Table("tbl_cost_center_type")]
public sealed class CostCenterType : ElementType
{
    /// <summary>
    /// Whether a node of this type is a company: it keeps its own books (documents, configuration, accounts) for itself and the nodes below
    /// it. A company is the root of its tree or directly under the root, never under another company (the founder's rule, 2026-10-29).
    /// </summary>
    public bool KeepsBooks { get; set; }

    /// <summary>Its cost centres are bank accounts: bank statements are uploaded for them.</summary>
    public bool TakesBankStatements { get; set; }

    /// <summary>Its cost centres make what is ordered: a kitchen, a bar, an oven. They have a production point display.</summary>
    public bool IsProductionPoint { get; set; }
}

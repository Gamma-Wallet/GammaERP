using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.CostCenters.Dto;

/// <summary>A type of cost centre, with its categories and properties, as the screens see it.</summary>
public sealed record CostCenterTypeDto : ElementTypeDto
{
    /// <summary>Whether a node of this type is a company (keeps its own books).</summary>
    public bool KeepsBooks { get; init; }
}

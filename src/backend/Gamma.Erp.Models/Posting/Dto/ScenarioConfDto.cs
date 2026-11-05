using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Posting.Dto;

public sealed record ScenarioConfDto : RowDto
{
    public Guid? ScenarioId { get; init; }

    public Guid? ChartId { get; init; }

    public string? ChartName { get; init; }

    public bool Side { get; init; }

    public string? SideValue { get; init; }

    /// <summary>Old value.</summary>
    public int? Value { get; init; }

    public string? ValueName { get; init; }

    /// <summary>The amount the line takes: a "Transaction amount" row of tbl_elements.</summary>
    public Guid? ValueElementsId { get; init; }

    /// <summary>Where the line is written: 1 from side, 2 to side, 3 on the transaction's element as the account.</summary>
    public int? Place { get; init; }

    /// <summary>The cost centre the entry carries: 1 from side, 2 to side, empty for none.</summary>
    public int? CostCenterPlace { get; init; }
}

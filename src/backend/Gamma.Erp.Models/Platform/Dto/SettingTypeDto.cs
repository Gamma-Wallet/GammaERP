using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Platform.Dto;

public sealed record SettingTypeDto : RowDto
{
    public bool IsElement { get; init; }

    public string? Code { get; init; }

    public int? CodeStart { get; init; }

    public int? CodeEnd { get; init; }

    public int? CodeCurrent { get; init; }

    public Dictionary<string, string>? Name { get; init; }

    public Dictionary<string, string>? Description { get; init; }

    public double SortOrder { get; init; }
}

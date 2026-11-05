using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Platform.Dto;

public sealed record SettingTypeInfoDto : RowDto
{
    public Guid? TypeId { get; init; }

    public Dictionary<string, string>? Name { get; init; }

    public Dictionary<string, string>? Description { get; init; }

    public bool Required { get; init; }

    public int? Kind { get; init; }

    public double SortOrder { get; init; }

    public Guid? ElementsId { get; init; }

    public bool IsFile { get; init; }
}

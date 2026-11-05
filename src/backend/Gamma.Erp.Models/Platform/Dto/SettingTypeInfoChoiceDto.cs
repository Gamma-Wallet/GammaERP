using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Platform.Dto;

public sealed record SettingTypeInfoChoiceDto : RowDto
{
    public Guid? TypeInfoId { get; init; }

    public Dictionary<string, string>? Name { get; init; }

    public Dictionary<string, string>? Description { get; init; }
}

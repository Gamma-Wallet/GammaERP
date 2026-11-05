using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Platform.Dto;

public sealed record ElementsTypeDto : RowDto
{
    public Dictionary<string, string>? Name { get; init; }
}

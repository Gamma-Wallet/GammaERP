using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Platform.Dto;

public sealed record ReportDto : RowDto
{
    public Dictionary<string, string>? Name { get; init; }

    public string? Code { get; init; }

    public bool IsElement { get; init; }

    public int? Conf { get; init; }

    public string? ElementName { get; init; }

    public string? ElementTable { get; init; }

    public string? Table1 { get; init; }

    public string? Table2 { get; init; }
}

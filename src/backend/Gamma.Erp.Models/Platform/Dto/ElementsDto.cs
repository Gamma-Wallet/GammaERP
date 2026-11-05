using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Platform.Dto;

public sealed record ElementsDto : RowDto
{
    public Guid? ElementsTypeId { get; init; }

    public Dictionary<string, string>? Name { get; init; }

    public string? Table { get; init; }

    public string? Category { get; init; }

    public Guid? CategoryId { get; init; }

    public string? Type { get; init; }

    public Guid? TypeId { get; init; }

    public string? Record { get; init; }

    public Guid? RecordId { get; init; }

    public int? DocumentConf { get; init; }

    /// <summary>The number the row is known by, e.g. a "Transaction amount" row's old value number.</summary>
    public int? Code { get; init; }
}

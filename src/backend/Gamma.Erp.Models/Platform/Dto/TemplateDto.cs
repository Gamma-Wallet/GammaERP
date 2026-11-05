using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Platform.Dto;

public sealed record TemplateDto : RowDto
{
    public Guid? DocumentTypeId { get; init; }

    public string? ShortName { get; init; }

    public string? LongName { get; init; }

    public bool Pos { get; init; }

    public string? Body { get; init; }

    public string? PrinterName { get; init; }

    public int? Copies { get; init; }
}

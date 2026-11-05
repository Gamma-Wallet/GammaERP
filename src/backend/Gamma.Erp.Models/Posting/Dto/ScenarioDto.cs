using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Posting.Dto;

public sealed record ScenarioDto : RowDto
{
    public Dictionary<string, string>? Name { get; init; }

    public Dictionary<string, string>? Description { get; init; }

    public Guid? DocumentTypeTransactionTypeId { get; init; }

    public Guid? ElementsId { get; init; }
}

using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Products.Dto;

public sealed record InterProductUnitDto : RowDto
{
    public Guid? ProductId { get; init; }

    public Guid? UnitId { get; init; }
}

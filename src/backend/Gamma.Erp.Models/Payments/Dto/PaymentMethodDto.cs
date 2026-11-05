using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Payments.Dto;

public sealed record PaymentMethodDto : RowDto
{
    public string? Code { get; init; }

    public Dictionary<string, string>? Name { get; init; }

    public Dictionary<string, string>? Description { get; init; }
}

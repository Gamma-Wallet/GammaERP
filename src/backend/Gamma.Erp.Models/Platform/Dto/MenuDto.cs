using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Models.Platform.Dto;

/// <summary>A node of the company's menu.</summary>
public sealed record MenuDto : RowDto
{
    public Dictionary<string, string>? Name { get; init; }

    public string? Code { get; init; }

    public bool IsElement { get; init; }

    public double SortOrder { get; init; }

    /// <summary>The key of the screen the item opens.</summary>
    public string? Screen { get; init; }

    /// <summary>What the screen shows.</summary>
    public string? Parameter { get; init; }

    /// <summary>For a documents item: the category whose document types it lists.</summary>
    public Guid? DocumentCategoryId { get; init; }

    public string? Icon { get; init; }
}

using Gamma.Erp.Models.Base.Dto;

namespace Gamma.Erp.Services.Entities;

/// <summary>An entity's lists, whatever its tables, for what works on all of them alike (the migration).</summary>
public interface IEntityRows
{
    Task<IReadOnlyList<CategoryDto>> CategoriesAsync(CancellationToken ct);

    Task<CategoryDto> SaveCategoryAsync(CategoryDto dto, CancellationToken ct);

    Task<IReadOnlyList<ElementTypeDto>> TypesAsync(CancellationToken ct);

    Task<ElementTypeDto> SaveTypeAsync(ElementTypeDto dto, CancellationToken ct);

    /// <summary>Copies properties of another type onto a type (all, or those named).</summary>
    Task<ElementTypeDto> CopyPropertiesAsync(Guid typeId, Guid fromTypeId, IReadOnlyList<Guid>? propertyIds, CancellationToken ct);

    Task<ElementDto> SaveElementAsync(ElementDto dto, CancellationToken ct);

    /// <summary>One element, null when the request may not see it.</summary>
    Task<ElementDto?> ElementAsync(Guid id, CancellationToken ct);

    /// <summary>Switches an element off (nothing is deleted).</summary>
    Task DeleteElementAsync(Guid id, CancellationToken ct);

    /// <summary>An element's earlier versions.</summary>
    Task<IReadOnlyList<HistoryEntryDto>> HistoryAsync(Guid id, CancellationToken ct);

    /// <summary>A page of the elements the request may see, as the lookups list them.</summary>
    Task<PageDto<LookupOptionDto>> LookupAsync(Guid? categoryId, Guid? typeId, string? search, string language, int page, int pageSize, CancellationToken ct);

    Task<IReadOnlyList<ElementDto>> AllElementsAsync(CancellationToken ct);

    Task<int> CountElementsAsync(CancellationToken ct);
}

using System.Text.Json.Nodes;

namespace Gamma.Erp.Models.Base.Dto;

/// <summary>An opportunity's own fields.</summary>
public sealed record OpportunityFieldsDto(Guid? PartnerId, Guid? StageId, decimal? Amount, Guid? CurrencyId, DateOnly? ExpectedClose, Guid? OwnerUserId, string? LostReason);

/// <summary>
/// What every entity's screens and API exchange, whatever the entity. A text is one value per language ({"en": "…", "sq": "…"}). The
/// tenant and the history link (id_change) are kept by the system and never exchanged.
/// </summary>
public abstract record RowDto
{
    public Guid? Id { get; init; }

    /// <summary>The cost centre the row belongs to (the corporate, a company or one of its cost centres).</summary>
    public Guid? OwnerId { get; init; }

    /// <summary>The parent row of the same table (a tree); null at the top.</summary>
    public Guid? IdSup { get; init; }

    public bool Active { get; init; } = true;

    /// <summary>When this version was written, and by whom; set by the system.</summary>
    public DateTimeOffset? CreatedOn { get; init; }

    public Guid? CreatedBy { get; init; }
}

/// <summary>A category of an entity.</summary>
public record CategoryDto : RowDto
{
    public bool IsElement { get; init; }

    public string? Code { get; init; }

    public Dictionary<string, string> Name { get; init; } = [];

    public Dictionary<string, string>? Description { get; init; }

    public double SortOrder { get; init; }
}

/// <summary>A choice of a list property.</summary>
public record TypeInfoChoiceDto : RowDto
{
    public Dictionary<string, string> Name { get; init; } = [];

    public Dictionary<string, string>? Description { get; init; }
}

/// <summary>A dynamic property of a type, with its choices when it is a list.</summary>
public record TypeInfoDto : RowDto
{
    public Dictionary<string, string> Name { get; init; } = [];

    /// <summary>The fixed key the engine finds the property by, if any.</summary>
    public string? Code { get; init; }

    public Dictionary<string, string>? Description { get; init; }

    public bool Required { get; init; }

    public PropertyKind Kind { get; init; } = PropertyKind.Text;

    public double SortOrder { get; init; }

    /// <summary>For a lookup: the elements row (entity, category, type) it picks from.</summary>
    public Guid? ElementsId { get; init; }

    public bool IsFile { get; init; }

    /// <summary>The activating-list choice of the same type that shows this property (empty: always shown).</summary>
    public Guid? ShownForChoiceId { get; init; }

    public IReadOnlyList<TypeInfoChoiceDto> Choices { get; init; } = [];
}

/// <summary>A type of an entity, with the categories it is in and its properties.</summary>
public record ElementTypeDto : RowDto
{
    public bool IsElement { get; init; }

    /// <summary>Products only: each unit has a serial number.</summary>
    public bool TracksSerial { get; init; }

    /// <summary>For a product type: its products move lot by lot.</summary>
    public bool TracksLot { get; init; }

    /// <summary>For a product type: a lot past its expiry date can't go to a client.</summary>
    public bool RefusesExpired { get; init; } = true;

    /// <summary>For a cost centre type: its cost centres are bank accounts that take statements.</summary>
    public bool TakesBankStatements { get; init; }

    /// <summary>For a cost centre type: its cost centres are production points (kitchen, bar).</summary>
    public bool IsProductionPoint { get; init; }

    /// <summary>For a product type: its products are fixed assets (registered and depreciated).</summary>
    public bool FixedAsset { get; init; }

    /// <summary>Products only: the VAT group its products' lines start with.</summary>
    public Guid? VatGroupId { get; init; }

    public string? Code { get; init; }

    public int? CodeStart { get; init; }

    public int? CodeEnd { get; init; }

    public int? CodeCurrent { get; init; }

    public Dictionary<string, string> Name { get; init; } = [];

    public Dictionary<string, string>? Description { get; init; }

    public double SortOrder { get; init; }

    /// <summary>The categories the type is in (many to many).</summary>
    public IReadOnlyList<Guid> CategoryIds { get; init; } = [];

    public IReadOnlyList<TypeInfoDto> Properties { get; init; } = [];
}

/// <summary>An element of an entity: its columns, the categories it is in, its property values and who may see it.</summary>
public record ElementDto : RowDto
{
    public Guid TypeId { get; init; }

    public string? Code { get; init; }

    public Dictionary<string, string> Name { get; init; } = [];

    public Dictionary<string, string>? Description { get; init; }

    public bool IsComposite { get; init; }

    /// <summary>Products only: the VAT group its lines start with, over its type's.</summary>
    public Guid? DefaultVatGroupId { get; init; }

    /// <summary>On a partner: the days it has to pay.</summary>
    public int? PaymentDays { get; init; }

    /// <summary>On an opportunity: its own fields.</summary>
    public OpportunityFieldsDto? Opportunity { get; init; }

    /// <summary>The categories the element is in (many to many).</summary>
    public IReadOnlyList<Guid> CategoryIds { get; init; } = [];

    /// <summary>The property values: property (type info) id → the value as JSON of the property's kind.</summary>
    public Dictionary<Guid, JsonNode?> Values { get; init; } = [];

    /// <summary>The users who may see the element; with <see cref="AccessRoleIds"/> empty too, everyone with the right to the entity.</summary>
    public IReadOnlyList<Guid> AccessUserIds { get; init; } = [];

    /// <summary>The roles (user categories) whose users may see the element.</summary>
    public IReadOnlyList<Guid> AccessRoleIds { get; init; } = [];
}

/// <summary>One page of a list, and how many there are in all.</summary>
public sealed record PageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    /// <summary>The number of pages.</summary>
    public int PageCount => PageSize <= 0 ? 0 : (Total + PageSize - 1) / PageSize;
}

/// <summary>One choice of a lookup: an element (or a currency) by id, with its code and its name in the language asked for.</summary>
public sealed record LookupOptionDto(Guid Id, string? Code, string Name);

/// <summary>
/// A scope a lookup property picks from (an elements row): a whole entity, or one of its categories, or a category and a type. Name is the
/// row's own name in the language asked for, else empty.
/// </summary>
public sealed record LookupScopeDto(Guid Id, string Table, Guid? CategoryId, Guid? TypeId, string Name);

/// <summary>One earlier state of a row (a disabled copy in the same table), newest first in a history list.</summary>
public sealed record HistoryEntryDto(Guid Id, DateTimeOffset CreatedOn, Guid? CreatedBy, JsonObject Row);

/// <summary>Copying properties onto a type: from which type of the same entity, and which of its properties (empty: all).</summary>
public sealed record CopyPropertiesRequest(Guid FromTypeId, IReadOnlyList<Guid>? PropertyIds = null);

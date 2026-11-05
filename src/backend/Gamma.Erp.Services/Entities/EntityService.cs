using System.Text.Json;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Entities;

/// <summary>
/// The categories, types (with their properties and choices) and elements of one entity, with their rules. Written once for every entity: partners, products,
/// cost centres, documents, transactions, charts, units, employees, users.
/// <list type="bullet">
/// <item>Visibility: the request's node and the nodes below it, never above (<see cref="Visibility"/>).</item>
/// <item>Lists are per category; read all / write / forward show every element of it, read own only those given to the user or the
/// user's roles (the element access).</item>
/// <item>Trees: a parent of the same table, never the row itself or one below it; an element without a parent needs a top-level type,
/// otherwise its type must be a child of the parent element's type (<see cref="ErrorCodes.TypeError"/>).</item>
/// <item>No two active siblings with the same name (<see cref="ErrorCodes.NameError"/>); a row with active children is not switched off
/// (<see cref="ErrorCodes.ChildrenExistError"/>); a category with elements is not moved (<see cref="ErrorCodes.ElementsExistError"/>); a
/// type's codes stay within its range (<see cref="ErrorCodes.CodeLimitError"/>).</item>
/// <item>Nothing is deleted: rows are switched off (active = false), never removed, and every change keeps the old state as a disabled
/// copy (the history interceptor).</item>
/// </list>
/// </summary>
public sealed class EntityService<TCategory, TType, TTypeCategory, TTypeInfo, TChoice, TElement, TInfo, TElementCategory, TAccess>(
    ErpDbContext db, RequestContext request, Visibility visibility) : IEntityRows
    where TCategory : Category, new()
    where TType : ElementType, new()
    where TTypeCategory : InterTypeCategory, new()
    where TTypeInfo : TypeInfo, new()
    where TChoice : TypeInfoChoice, new()
    where TElement : Element, new()
    where TInfo : ElementInfo, new()
    where TElementCategory : InterElementCategory, new()
    where TAccess : ElementAccess, new()
{
    /// <summary>The entity's element table, as the rights name it (tbl_elements.table).</summary>
    public string Table { get; } = db.Model.FindEntityType(typeof(TElement))!.GetTableName()!;

    /// <summary>
    /// Whether the code is part of what makes a new element unique, beside its name:
    /// documents of a type all carry the type's name, and their codes tell them apart.
    /// </summary>
    public bool CodeMakesUnique { get; init; }

    /// <summary>Sets the entity's own columns, beyond the element's, before an element is saved (e.g. a document's company and dates); true for a new one.</summary>
    public Action<TElement, ElementDto, bool>? Fill { get; init; }

    /// <summary>A rule of the entity's own, checked before an element is saved (e.g. where a company may sit in the cost centre tree).</summary>
    public Func<ElementDto, CancellationToken, Task>? Check { get; init; }

    // ---------- Categories ----------

    /// <summary>The categories the request uses: its own and below, and its company's (configuration applies to all of a company's cost centres).</summary>
    public async Task<IReadOnlyList<CategoryDto>> CategoriesAsync(CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        return [.. (await db.Set<TCategory>().AsNoTracking().Where(c => c.OwnerId == null || nodes.Contains(c.OwnerId.Value))
            .OrderBy(c => c.SortOrder).ToListAsync(ct).ConfigureAwait(false)).Select(ToDto)];
    }

    public async Task<CategoryDto> SaveCategoryAsync(CategoryDto dto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var name = Text(dto.Name) ?? throw ValidationException.For("name", "A name is required.");
        var all = await db.Set<TCategory>().ToListAsync(ct).ConfigureAwait(false);
        var category = dto.Id is { } id ? await VisibleAsync(all.FirstOrDefault(c => c.Id == id), ct).ConfigureAwait(false) : null;
        if (dto.Id is not null && category is null)
        {
            throw ValidationException.For("id", "The category does not exist.");
        }

        CheckParent(all, c => c.Id, c => c.IdSup, category?.Id, dto.IdSup, "category");
        // Only the categories the request sees: another company's of the same name is its own (a group's companies each have "Weight").
        var seen = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var siblings = all.Where(c => c.IdSup == dto.IdSup && c.Active && c.Id != category?.Id && (c.OwnerId is null || seen.Contains(c.OwnerId.Value))).ToList();
        if (category is null)
        {
            if (siblings.Any(c => SameName(c.Name, name)))
            {
                throw ValidationException.For("name", $"{ErrorCodes.NameError}: a category with this name is already at this level.");
            }

            category = new TCategory { OwnerId = request.NodeId, SortOrder = dto.SortOrder == 0 ? all.Count + 1 : dto.SortOrder };
            db.Add(category);
        }
        else
        {
            if (!dto.Active && all.Any(c => c.IdSup == category.Id && c.Active))
            {
                throw ValidationException.For("active", $"{ErrorCodes.ChildrenExistError}: the category has active categories under it.");
            }

            if (dto.IdSup != category.IdSup && await db.Set<TElementCategory>().AnyAsync(l => l.CategoryId == category.Id, ct).ConfigureAwait(false))
            {
                throw ValidationException.For("idSup", $"{ErrorCodes.ElementsExistError}: the category has elements; it cannot be moved.");
            }

            if (siblings.Any(c => SameName(c.Name, name) && SameText(c.Description, Text(dto.Description))))
            {
                throw ValidationException.For("name", $"{ErrorCodes.NameError}: a category with this name and description is already at this level.");
            }

            category.SortOrder = dto.SortOrder;
        }

        (category.IdSup, category.IsElement, category.Code, category.Name, category.Description, category.Active) =
            (dto.IdSup, dto.IsElement, dto.Code, name, Text(dto.Description), dto.Active);
        TranslatedText.Complete(category, await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(category);
    }

    // ---------- Types, with their categories, properties and choices ----------

    /// <summary>The types the request uses: its own and below, and its company's.</summary>
    public async Task<IReadOnlyList<ElementTypeDto>> TypesAsync(CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var types = await db.Set<TType>().AsNoTracking().Where(t => t.OwnerId == null || nodes.Contains(t.OwnerId.Value)).OrderBy(t => t.SortOrder)
            .ToListAsync(ct).ConfigureAwait(false);
        var ids = types.Select(t => t.Id).ToList();
        var links = (await db.Set<TTypeCategory>().AsNoTracking().Where(l => ids.Contains(l.TypeId)).ToListAsync(ct).ConfigureAwait(false)).ToLookup(l => l.TypeId);
        // A shared type's properties and choices belong to one company each (a company's pack adds its own): only the request's are listed.
        var properties = (await db.Set<TTypeInfo>().AsNoTracking().Where(p => ids.Contains(p.TypeId) && (p.OwnerId == null || nodes.Contains(p.OwnerId.Value)))
            .OrderBy(p => p.SortOrder).ToListAsync(ct).ConfigureAwait(false)).ToLookup(p => p.TypeId);
        var propertyIds = properties.SelectMany(p => p).Select(p => p.Id).ToList();
        var choices = (await db.Set<TChoice>().AsNoTracking().Where(c => propertyIds.Contains(c.TypeInfoId) && (c.OwnerId == null || nodes.Contains(c.OwnerId.Value)))
            .ToListAsync(ct).ConfigureAwait(false)).ToLookup(c => c.TypeInfoId);
        return [.. types.Select(t => ToDto(t, links[t.Id], properties[t.Id], choices))];
    }

    public async Task<ElementTypeDto> SaveTypeAsync(ElementTypeDto dto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var name = Text(dto.Name) ?? throw ValidationException.For("name", "A name is required.");
        var all = await db.Set<TType>().ToListAsync(ct).ConfigureAwait(false);
        var type = dto.Id is { } id ? await VisibleAsync(all.FirstOrDefault(t => t.Id == id), ct).ConfigureAwait(false) : null;
        if (dto.Id is not null && type is null)
        {
            throw ValidationException.For("id", "The type does not exist.");
        }

        CheckParent(all, t => t.Id, t => t.IdSup, type?.Id, dto.IdSup, "type");
        // Only the types the request sees, as for categories.
        var seenTypes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var siblings = all.Where(t => t.IdSup == dto.IdSup && t.Active && t.Id != type?.Id && (t.OwnerId is null || seenTypes.Contains(t.OwnerId.Value))).ToList();
        if (type is null && siblings.Any(t => SameName(t.Name, name)))
        {
            throw ValidationException.For("name", $"{ErrorCodes.NameError}: a type with this name is already at this level.");
        }

        if (dto.CodeStart is { } start && dto.CodeEnd is { } end && !(start <= (dto.CodeCurrent ?? start) && (dto.CodeCurrent ?? start) < end))
        {
            throw ValidationException.For("codeCurrent", $"{ErrorCodes.CodeLimitError}: the next code must lie between the first and the last.");
        }

        var categories = await db.Set<TCategory>().Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        if (dto.CategoryIds.Except(categories).Any())
        {
            throw ValidationException.For("categoryIds", "A category does not exist.");
        }

        var duplicate = dto.Properties.Where(p => p.Active).Select(p => Text(p.Name)?.In(TranslatedText.DefaultLanguage)?.ToUpperInvariant())
            .GroupBy(n => n).FirstOrDefault(g => g.Key is not null && g.Count() > 1);
        if (duplicate is not null)
        {
            throw ValidationException.For("properties", $"{ErrorCodes.NameError}: the property {duplicate.Key} is there twice.");
        }

        await CheckShownForAsync(type?.Id, dto.Properties, ct).ConfigureAwait(false);
        if (type is null)
        {
            type = new TType { OwnerId = request.NodeId };
            db.Add(type);
        }
        else if (!dto.Active && all.Any(t => t.IdSup == type.Id && t.Active))
        {
            throw ValidationException.For("active", $"{ErrorCodes.ChildrenExistError}: the type has active types under it.");
        }

        if ((object)type is Gamma.Erp.Models.CostCenters.CostCenterType centres)
        {
            (centres.TakesBankStatements, centres.IsProductionPoint) = (dto.TakesBankStatements, dto.IsProductionPoint);
        }

        if ((object)type is Gamma.Erp.Models.Products.ProductType products)
        {
            products.TracksSerial = dto.TracksSerial;
            (products.TracksLot, products.RefusesExpired) = (dto.TracksLot, dto.RefusesExpired);
            products.VatGroupId = dto.VatGroupId;
            products.FixedAsset = dto.FixedAsset;
        }

        (type.IdSup, type.IsElement, type.Code, type.CodeStart, type.CodeEnd, type.CodeCurrent, type.Name, type.Description, type.SortOrder, type.Active) =
            (dto.IdSup, dto.IsElement, dto.Code, dto.CodeStart, dto.CodeEnd, dto.CodeCurrent ?? dto.CodeStart, name, Text(dto.Description),
             dto.SortOrder == 0 ? all.Count + 1 : dto.SortOrder, dto.Active);
        TranslatedText.Complete(type, await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await LinkAsync<TTypeCategory>(l => l.TypeId == type.Id, l => l.CategoryId, dto.CategoryIds,
            c => new TTypeCategory { TypeId = type.Id, CategoryId = c, OwnerId = type.OwnerId }, ct).ConfigureAwait(false);
        await PropertiesAsync(type, dto.Properties, ct).ConfigureAwait(false);
        return (await TypesAsync(ct).ConfigureAwait(false)).First(t => t.Id == type.Id);
    }

    /// <summary>
    /// The type's properties become those sent, among the ones the request sees: another company's properties and choices on a shared type are
    /// never switched off or changed. A new one belongs to the type's owner, or on a shared type to the request's company (the founder,
    /// 2026-10-31), as a company's pack rows do.
    /// </summary>
    private async Task PropertiesAsync(TType type, IReadOnlyList<TypeInfoDto> wanted, CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var owner = type.OwnerId ?? await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var existing = await db.Set<TTypeInfo>().Where(p => p.TypeId == type.Id && (p.OwnerId == null || nodes.Contains(p.OwnerId.Value)))
            .ToListAsync(ct).ConfigureAwait(false);
        if (wanted.Select((w, i) => (w, i)).FirstOrDefault(x => x.w.Id is { } id && existing.All(p => p.Id != id)) is { w: not null } unknown)
        {
            throw ValidationException.For($"properties[{unknown.i}].id", "The property does not exist.");
        }

        foreach (var gone in existing.Where(p => p.Active && wanted.All(w => w.Id != p.Id)))
        {
            gone.Active = false;
        }

        foreach (var (dto, i) in wanted.Select((w, i) => (w, i)))
        {
            var property = dto.Id is { } id ? existing.FirstOrDefault(p => p.Id == id) : null;
            if (property is null)
            {
                property = new TTypeInfo { TypeId = type.Id, OwnerId = owner };
                db.Add(property);
            }

            if (!Enum.IsDefined(dto.Kind))
            {
                throw ValidationException.For($"properties[{i}].kind", "Unknown kind.");
            }

            property.Code = string.IsNullOrWhiteSpace(dto.Code) ? null : dto.Code.Trim().ToUpperInvariant();
            (property.Name, property.Description, property.Required, property.Kind, property.SortOrder, property.ElementsId, property.IsFile, property.ShownForChoiceId,
                    property.Active) =
                (Text(dto.Name) ?? throw ValidationException.For($"properties[{i}].name", "A name is required."), Text(dto.Description), dto.Required, dto.Kind,
                 dto.SortOrder == 0 ? i + 1 : dto.SortOrder, dto.ElementsId, dto.IsFile, dto.ShownForChoiceId, dto.Active);
            TranslatedText.Complete(property, await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            var choices = await db.Set<TChoice>().Where(c => c.TypeInfoId == property.Id && (c.OwnerId == null || nodes.Contains(c.OwnerId.Value)))
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var gone in choices.Where(c => c.Active && dto.Choices.All(w => w.Id != c.Id)))
            {
                gone.Active = false;
            }

            foreach (var choiceDto in dto.Choices)
            {
                var choice = choiceDto.Id is { } cid ? choices.FirstOrDefault(c => c.Id == cid) : null;
                if (choice is null)
                {
                    choice = new TChoice { TypeInfoId = property.Id, OwnerId = property.OwnerId ?? owner };
                    db.Add(choice);
                }

                (choice.Name, choice.Description, choice.Active) = (Text(choiceDto.Name) ?? throw ValidationException.For($"properties[{i}].choices", "A choice needs a name."),
                    Text(choiceDto.Description), choiceDto.Active);
                TranslatedText.Complete(choice, await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false));
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A property shown for a choice names a choice of an activating list of the same type, saved before (a list and the
    /// properties it shows are saved in two steps, since a new choice gets its id when it is saved), and never one of its own.
    /// </summary>
    private async Task CheckShownForAsync(Guid? typeId, IReadOnlyList<TypeInfoDto> wanted, CancellationToken ct)
    {
        var linked = wanted.Select((p, i) => (p, i)).Where(x => x.p.Active && x.p.ShownForChoiceId is not null).ToList();
        if (linked.Count == 0)
        {
            return;
        }

        var lists = wanted.Where(p => p.Active && p.Kind == PropertyKind.ActivatingList && p.Id is not null).Select(p => p.Id!.Value).ToHashSet();
        var owners = typeId is null ? [] : await (from c in db.Set<TChoice>().AsNoTracking()
                                                  join p in db.Set<TTypeInfo>().AsNoTracking() on c.TypeInfoId equals p.Id
                                                  where p.TypeId == typeId
                                                  select new { c.Id, c.TypeInfoId }).ToDictionaryAsync(c => c.Id, c => c.TypeInfoId, ct).ConfigureAwait(false);
        foreach (var (p, i) in linked)
        {
            if (!owners.TryGetValue(p.ShownForChoiceId!.Value, out var list) || !lists.Contains(list) || list == p.Id)
            {
                throw ValidationException.For($"properties[{i}].shownForChoiceId", "It must be a saved choice of another activating list of this type.");
            }
        }
    }

    /// <summary>
    /// Copies properties of another type of the entity onto a type (a preset such as brand / model / serial number shared
    /// by fridge, TV and washing machine), with their choices and the activating lists' links; all of them, or those named. A property the type
    /// already has (by name) is left as it is. Saved through <see cref="SaveTypeAsync"/>, so the type's rules apply.
    /// </summary>
    public async Task<ElementTypeDto> CopyPropertiesAsync(Guid typeId, Guid fromTypeId, IReadOnlyList<Guid>? propertyIds, CancellationToken ct)
    {
        var types = await TypesAsync(ct).ConfigureAwait(false);
        var target = types.FirstOrDefault(t => t.Id == typeId) ?? throw ValidationException.For("id", "The type does not exist.");
        var source = types.FirstOrDefault(t => t.Id == fromTypeId) ?? throw ValidationException.For("fromTypeId", "The type to copy from does not exist.");
        if (propertyIds?.Except(source.Properties.Select(p => p.Id!.Value)).Any() == true)
        {
            throw ValidationException.For("propertyIds", "A property is not one of the type to copy from.");
        }

        var have = target.Properties.Where(p => p.Active).Select(Key).ToHashSet();
        var copied = source.Properties.Where(p => p.Active && (propertyIds is null || propertyIds.Contains(p.Id!.Value)) && !have.Contains(Key(p))).ToList();
        if (copied.Count == 0)
        {
            return target;
        }

        // First the properties and their choices, then the links to the choices they now have (a new choice's id is known once it is saved).
        var order = target.Properties.Count == 0 ? 0 : target.Properties.Max(p => p.SortOrder);
        var saved = await SaveTypeAsync(target with
        {
            Properties = [.. target.Properties, .. copied.Select((p, n) => p with
            {
                Id = null, OwnerId = null, SortOrder = order + n + 1, ShownForChoiceId = null,
                Choices = [.. p.Choices.Where(c => c.Active).Select(c => c with { Id = null })],
            })],
        }, ct).ConfigureAwait(false);
        var sourceChoices = source.Properties.SelectMany(p => p.Choices.Select(c => (Property: Key(p), Choice: Key(c.Name)))).ToList();
        var choiceIds = source.Properties.SelectMany(p => p.Choices).Select(c => c.Id!.Value).ToList();
        var linked = copied.Where(p => p.ShownForChoiceId is { } c && choiceIds.Contains(c))
            .ToDictionary(p => Key(p)!, p => sourceChoices[choiceIds.IndexOf(p.ShownForChoiceId!.Value)]);
        if (linked.Count == 0)
        {
            return saved;
        }

        Guid? NewChoice((string? Property, string? Choice) from) =>
            saved.Properties.FirstOrDefault(l => l.Active && Key(l) == from.Property)?.Choices.FirstOrDefault(c => c.Active && Key(c.Name) == from.Choice)?.Id;
        return await SaveTypeAsync(saved with
        {
            Properties = [.. saved.Properties.Select(p => p.Active && Key(p) is { } k && linked.TryGetValue(k, out var from) && p.ShownForChoiceId is null
                ? p with { ShownForChoiceId = NewChoice(from) }
                : p)],
        }, ct).ConfigureAwait(false);
    }

    private static string? Key(TypeInfoDto property) => Key(property.Name);

    private static string? Key(Dictionary<string, string> name) => name.GetValueOrDefault(TranslatedText.DefaultLanguage)?.Trim().ToUpperInvariant();

    // ---------- Elements ----------

    /// <summary>The largest page a list returns.</summary>
    public const int MaxPageSize = 500;

    /// <summary>
    /// One page of the elements of a category, optionally of one type and matching a name or code. Paged in the
    /// database: page numbers from 1, in the order the rows were made (the ids are
    /// time-ordered), with the number of pages.
    /// </summary>
    public async Task<PageDto<ElementDto>> ElementsAsync(Guid categoryId, Guid? typeId, string? search, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));
        if (await VisibleInCategoryAsync(categoryId, search, ct).ConfigureAwait(false) is not { } query)
        {
            return new PageDto<ElementDto>([], 0, page, pageSize);
        }

        if (typeId is { } t)
        {
            query = query.Where(e => e.TypeId == t);
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderBy(e => e.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return new PageDto<ElementDto>([.. rows.Select(e => ToDto(e, [], [], [], []))], total, page, pageSize);
    }

    /// <summary>
    /// The active elements of a category the request may see (its rights on the category, its nodes — what they own, and the cost centres that
    /// are themselves one of them (B15) — and for "read own" only the elements given to the user or one of the user's roles), matching the
    /// search when there is one; null when it may not read the category at all.
    /// </summary>
    /// <summary>The rows the request's nodes hold that are in no active category (a user whose roles were all switched off); administrators only.</summary>
    public async Task<IReadOnlyList<ElementDto>> WithoutCategoryAsync(CancellationToken ct)
    {
        if (!request.IsAdministrator)
        {
            return [];
        }

        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var activeCategories = db.Set<TCategory>().Where(c => c.Active).Select(c => c.Id);
        var linked = db.Set<TElementCategory>().Where(l => activeCategories.Contains(l.CategoryId)).Select(l => l.ElementId);
        var rows = await db.Set<TElement>().AsNoTracking().Where(e => e.Active && (e.OwnerId == null || nodes.Contains(e.OwnerId.Value)) && !linked.Contains(e.Id))
            .OrderBy(e => e.Code).Take(MaxPageSize).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(e => ToDto(e, [], [], [], []))];
    }

    public async Task<IQueryable<TElement>?> VisibleInCategoryAsync(Guid categoryId, string? search, CancellationToken ct)
    {
        var rights = await visibility.RightsAsync(Table, categoryId, ct).ConfigureAwait(false);
        if (!rights.SeesAll && !rights.ReadOwn)
        {
            return null;
        }

        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var inCategory = db.Set<TElementCategory>().Where(l => l.CategoryId == categoryId).Select(l => l.ElementId);
        var query = (string.IsNullOrWhiteSpace(search) ? db.Set<TElement>() : Matching(search.Trim())).AsNoTracking()
            .Where(e => (e.OwnerId == null || nodes.Contains(e.OwnerId.Value) || nodes.Contains(e.Id)) && inCategory.Contains(e.Id));
        if (!rights.SeesAll)
        {
            // Read own: only the elements given to the user or to one of the user's roles.
            var userId = request.UserId;
            var roles = request.RoleIds.ToList();
            var given = db.Set<TAccess>().Where(a => (a.UserId != null && a.UserId == userId) || (a.RoleId != null && roles.Contains(a.RoleId.Value)))
                .Select(a => a.ElementId);
            query = query.Where(e => given.Contains(e.Id));
        }

        return query;
    }

    /// <summary>
    /// The choices of a lookup property on this entity: the elements of one category, or of every category the
    /// request may read when none is named, optionally of one type and matching a code or name; by code, a page at a time. The same rights
    /// as the lists: read all / write / forward show the category's elements, read own only those given to the user or the user's roles.
    /// </summary>
    public async Task<PageDto<LookupOptionDto>> LookupAsync(Guid? categoryId, Guid? typeId, string? search, string language, int page, int pageSize,
        CancellationToken ct)
    {
        (page, pageSize) = (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));
        var categories = categoryId is { } one ? [one] : (await CategoriesAsync(ct).ConfigureAwait(false)).Where(c => c.Id is not null).Select(c => c.Id!.Value).ToList();
        var seesAll = new List<Guid>();
        var readsOwn = new List<Guid>();
        foreach (var category in categories)
        {
            var rights = await visibility.RightsAsync(Table, category, ct).ConfigureAwait(false);
            if (rights.SeesAll)
            {
                seesAll.Add(category);
            }
            else if (rights.ReadOwn)
            {
                readsOwn.Add(category);
            }
        }

        if (seesAll.Count == 0 && readsOwn.Count == 0)
        {
            return new PageDto<LookupOptionDto>([], 0, page, pageSize);
        }

        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var userId = request.UserId;
        var roles = request.RoleIds.ToList();
        var inAll = db.Set<TElementCategory>().Where(l => seesAll.Contains(l.CategoryId)).Select(l => l.ElementId);
        var inOwn = db.Set<TElementCategory>().Where(l => readsOwn.Contains(l.CategoryId)).Select(l => l.ElementId);
        var given = db.Set<TAccess>().Where(a => (a.UserId != null && a.UserId == userId) || (a.RoleId != null && roles.Contains(a.RoleId.Value)))
            .Select(a => a.ElementId);
        var query = (string.IsNullOrWhiteSpace(search) ? db.Set<TElement>() : Matching(search.Trim())).AsNoTracking()
            .Where(e => (e.OwnerId == null || nodes.Contains(e.OwnerId.Value) || nodes.Contains(e.Id)) && (inAll.Contains(e.Id) || (inOwn.Contains(e.Id) && given.Contains(e.Id))));
        if (typeId is { } t)
        {
            query = query.Where(e => e.TypeId == t);
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderBy(e => e.Code).ThenBy(e => e.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct).ConfigureAwait(false);
        return new PageDto<LookupOptionDto>([.. rows.Select(e => new LookupOptionDto(e.Id, e.Code, e.Name.In(language) ?? ""))], total, page, pageSize);
    }

    /// <summary>One element with its categories, values and access; null when the request may not see it.</summary>
    public async Task<ElementDto?> ElementAsync(Guid id, CancellationToken ct)
    {
        var element = await VisibleAsync(await db.Set<TElement>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        if (element is null)
        {
            return null;
        }

        var categories = await db.Set<TElementCategory>().AsNoTracking().Where(l => l.ElementId == id).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        var access = await db.Set<TAccess>().AsNoTracking().Where(a => a.ElementId == id).ToListAsync(ct).ConfigureAwait(false);
        // An administrator sees every row, as in the lists (a user who lost every role is opened to give it one).
        if (!request.IsAdministrator && !await MaySeeAsync(categories, access, ct).ConfigureAwait(false))
        {
            return null;
        }

        var values = await db.Set<TInfo>().AsNoTracking().Where(v => v.ElementId == id).ToListAsync(ct).ConfigureAwait(false);
        return ToDto(element, categories, values, access.Where(a => a.UserId is not null).Select(a => a.UserId!.Value), access.Where(a => a.RoleId is not null).Select(a => a.RoleId!.Value));
    }

    public async Task<ElementDto> SaveElementAsync(ElementDto dto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var name = Text(dto.Name) ?? throw ValidationException.For("name", "A name is required.");
        if (dto.CategoryIds.Count == 0)
        {
            throw ValidationException.For("categoryIds", "An element is in at least one category.");
        }

        if (!await MayWriteAsync(dto.CategoryIds, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("rights", "You may not write elements of this category.");
        }

        var type = await db.Set<TType>().FirstOrDefaultAsync(t => t.Id == dto.TypeId && t.Active, ct).ConfigureAwait(false);
        if (type is null || (type.OwnerId is { } owner && !(await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false)).Contains(owner)))
        {
            throw ValidationException.For("typeId", "The type does not exist.");
        }
        var element = dto.Id is { } id ? await VisibleAsync(await db.Set<TElement>().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false), ct).ConfigureAwait(false) : null;
        if (dto.Id is not null && element is null)
        {
            throw ValidationException.For("id", "The element does not exist.");
        }

        var parent = await ParentAsync(element?.Id, dto.IdSup, ct).ConfigureAwait(false);
        // Under a parent of the type above its own (a cash desk under a company), past folders of types (a restaurant, a kind of point of sale,
        // under a company), or of its own type (cm under m) — the founder, 2026-10-31.
        var above = type.IdSup;
        while (above is { } a && (parent is null || a != parent.TypeId)
            && await db.Set<TType>().AsNoTracking().Where(t => t.Id == a && !t.IsElement).Select(t => new { t.IdSup }).FirstOrDefaultAsync(ct).ConfigureAwait(false) is { } folder)
        {
            above = folder.IdSup;
        }

        if (parent is null ? above is not null : above != parent.TypeId && type.Id != parent.TypeId)
        {
            throw ValidationException.For("typeId", $"{ErrorCodes.TypeError}: an element's type must be a child of its parent's type or the same; one without a parent needs a top-level type.");
        }

        // Everything is checked before anything is added or a code is taken, so a refused save leaves nothing behind.
        if (Check is not null)
        {
            await Check(dto, ct).ConfigureAwait(false);
        }

        var values = await CheckValuesAsync(type.Id, dto.Values, ct).ConfigureAwait(false);
        // Only the siblings with the same name are read (the name is compared in SQL), never the whole table.
        var selfId = element?.Id;
        var idSup = dto.IdSup;
        var siblings = await Named(name).AsNoTracking().Where(e => e.IdSup == idSup && e.Id != selfId).ToListAsync(ct).ConfigureAwait(false);
        if (element is null)
        {
            var categoryIds = dto.CategoryIds.ToList();
            var candidates = siblings.Where(e => e.TypeId == dto.TypeId).Select(e => e.Id).ToList();
            var twins = await db.Set<TElementCategory>().Where(l => categoryIds.Contains(l.CategoryId) && candidates.Contains(l.ElementId))
                .Select(l => l.ElementId).ToListAsync(ct).ConfigureAwait(false);
            var code = string.IsNullOrWhiteSpace(dto.Code) ? null : dto.Code;
            if (siblings.Any(e => twins.Contains(e.Id)
                && (!CodeMakesUnique || (code is not null && string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase)))))
            {
                throw ValidationException.For("name", $"{ErrorCodes.NameError}: an element of this type with this name is already here in this category.");
            }

            element = new TElement { OwnerId = request.NodeId, Code = code ?? await NextCodeAsync(type, ct).ConfigureAwait(false) };
            db.Add(element);
        }
        else
        {
            if (!dto.Active && await db.Set<TElement>().AnyAsync(e => e.IdSup == element.Id, ct).ConfigureAwait(false))
            {
                throw ValidationException.For("active", $"{ErrorCodes.ChildrenExistError}: the element has active elements under it.");
            }

            var code = string.IsNullOrWhiteSpace(dto.Code) ? element.Code : dto.Code;
            if (siblings.Any(e => SameText(e.Description, Text(dto.Description))
                && (!CodeMakesUnique || string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase))))
            {
                throw ValidationException.For("name", $"{ErrorCodes.NameError}: an element with this name and description is already here.");
            }

            element.Code = string.IsNullOrWhiteSpace(dto.Code) ? element.Code : dto.Code;
        }

        (element.IdSup, element.TypeId, element.Name, element.Description, element.IsComposite, element.Active) =
            (dto.IdSup, dto.TypeId, name, Text(dto.Description), dto.IsComposite, dto.Active);
        TranslatedText.Complete(element, await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false));
        if (element is Gamma.Erp.Models.Products.Product product)
        {
            product.VatGroupId = dto.DefaultVatGroupId;
        }

        if (element is Gamma.Erp.Models.Partners.Partner partner)
        {
            if (dto.PaymentDays < 0)
            {
                throw ValidationException.For("paymentDays", "Payment days can't be below zero.");
            }

            partner.PaymentDays = dto.PaymentDays;
            if (!dto.Active && partner.Id != Guid.Empty)
            {
                await GeneralClientInUseAsync(partner.Id, ct).ConfigureAwait(false);
            }
        }

        if (element is Gamma.Erp.Models.Opportunities.Opportunity opportunity)
        {
            if (dto.Opportunity is not { PartnerId: not null, StageId: not null } fields)
            {
                throw ValidationException.For("opportunity", "An opportunity needs a client and a stage.");
            }

            // A stage whose outcome is lost (2) needs the reason, as a move on the Pipeline does.
            if (string.IsNullOrWhiteSpace(fields.LostReason)
                && await db.Set<Gamma.Erp.Models.Opportunities.OpportunityStage>().AsNoTracking().AnyAsync(s => s.Id == fields.StageId && s.Outcome == 2, ct).ConfigureAwait(false))
            {
                throw ValidationException.For("lostReason", "A lost opportunity needs the reason it was lost.");
            }

            (opportunity.PartnerId, opportunity.StageId, opportunity.Amount, opportunity.CurrencyId, opportunity.ExpectedClose, opportunity.OwnerUserId, opportunity.LostReason) =
                (fields.PartnerId, fields.StageId, fields.Amount, fields.CurrencyId, fields.ExpectedClose, fields.OwnerUserId, fields.LostReason);
        }

        Fill?.Invoke(element, dto, db.Entry(element).State == EntityState.Added);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await ValuesAsync(element, values, ct).ConfigureAwait(false);
        await LinkAsync<TElementCategory>(l => l.ElementId == element.Id, l => l.CategoryId, dto.CategoryIds,
            c => new TElementCategory { ElementId = element.Id, CategoryId = c, OwnerId = element.OwnerId }, ct).ConfigureAwait(false);
        await LinkAsync<TAccess>(a => a.ElementId == element.Id && a.UserId != null, a => a.UserId!.Value, dto.AccessUserIds,
            u => new TAccess { ElementId = element.Id, UserId = u, OwnerId = element.OwnerId }, ct).ConfigureAwait(false);
        await LinkAsync<TAccess>(a => a.ElementId == element.Id && a.RoleId != null, a => a.RoleId!.Value, dto.AccessRoleIds,
            r => new TAccess { ElementId = element.Id, RoleId = r, OwnerId = element.OwnerId }, ct).ConfigureAwait(false);
        return (await ElementAsync(element.Id, ct).ConfigureAwait(false))!;
    }

    /// <summary>Every element the request sees, with its categories and values (the migration's export), read at once.</summary>
    public async Task<IReadOnlyList<ElementDto>> AllElementsAsync(CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var elements = await db.Set<TElement>().AsNoTracking().Where(e => e.OwnerId == null || nodes.Contains(e.OwnerId.Value)).OrderBy(e => e.Code).ToListAsync(ct).ConfigureAwait(false);
        var ids = elements.Select(e => e.Id).ToList();
        var categories = (await db.Set<TElementCategory>().AsNoTracking().Where(l => ids.Contains(l.ElementId)).ToListAsync(ct).ConfigureAwait(false)).ToLookup(l => l.ElementId, l => l.CategoryId);
        var access = (await db.Set<TAccess>().AsNoTracking().Where(a => ids.Contains(a.ElementId)).ToListAsync(ct).ConfigureAwait(false)).ToLookup(a => a.ElementId);
        var values = (await db.Set<TInfo>().AsNoTracking().Where(v => ids.Contains(v.ElementId)).ToListAsync(ct).ConfigureAwait(false)).ToLookup(v => v.ElementId);
        var seen = new List<ElementDto>();
        foreach (var element in elements)
        {
            var given = access[element.Id].ToList();
            if (request.IsAdministrator || await MaySeeAsync([.. categories[element.Id]], given, ct).ConfigureAwait(false))
            {
                seen.Add(ToDto(element, [.. categories[element.Id]], [.. values[element.Id]], given.Where(a => a.UserId is not null).Select(a => a.UserId!.Value),
                    given.Where(a => a.RoleId is not null).Select(a => a.RoleId!.Value)));
            }
        }

        return seen;
    }

    /// <summary>How many elements the request's nodes hold.</summary>
    public async Task<int> CountElementsAsync(CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        return await db.Set<TElement>().AsNoTracking().CountAsync(e => e.OwnerId == null || nodes.Contains(e.OwnerId.Value), ct).ConfigureAwait(false);
    }

    /// <summary>Switches an element off (nothing is deleted); refused while active elements sit under it.</summary>
    /// <summary>A client that a point of sale's settings sell to (its general client) stays on while they point to it.</summary>
    private async Task GeneralClientInUseAsync(Guid partner, CancellationToken ct)
    {
        if (await db.Set<Gamma.Erp.Models.Products.PointOfSaleSetting>().AsNoTracking().AnyAsync(s => s.Active && s.WalkInClientId == partner, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("active", "This client is the general client of a point of sale's settings: choose another client there first.");
        }
    }

    public async Task DeleteElementAsync(Guid id, CancellationToken ct)
    {
        var element = await VisibleAsync(await db.Set<TElement>().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false), ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "The element does not exist.");
        var categories = await db.Set<TElementCategory>().Where(l => l.ElementId == id).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        if (!await MayWriteAsync(categories, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("rights", "You may not write elements of this category.");
        }

        if (typeof(TElement) == typeof(Gamma.Erp.Models.Partners.Partner))
        {
            await GeneralClientInUseAsync(id, ct).ConfigureAwait(false);
        }

        if (await db.Set<TElement>().AnyAsync(e => e.IdSup == id, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("active", $"{ErrorCodes.ChildrenExistError}: the element has active elements under it.");
        }

        element.Active = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Whether the request may write the element: it sees it and may write one of its categories.</summary>
    public async Task<bool> MayWriteElementAsync(Guid id, CancellationToken ct)
    {
        if (await VisibleAsync(await db.Set<TElement>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false), ct).ConfigureAwait(false) is null)
        {
            return false;
        }

        var categories = await db.Set<TElementCategory>().AsNoTracking().Where(l => l.ElementId == id).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        return await MayWriteAsync(categories, ct).ConfigureAwait(false);
    }

    /// <summary>The element's earlier states, newest first, following id_change through its disabled copies.</summary>
    public async Task<IReadOnlyList<HistoryEntryDto>> HistoryAsync(Guid id, CancellationToken ct)
    {
        if (await ElementAsync(id, ct).ConfigureAwait(false) is null)
        {
            return [];
        }

        var history = new List<HistoryEntryDto>();
        var cursor = (await db.Set<TElement>().AsNoTracking().FirstAsync(e => e.Id == id, ct).ConfigureAwait(false)).IdChange;
        while (cursor is { } c)
        {
            var copy = await db.Set<TElement>().IgnoreQueryFilters().AsNoTracking().FirstAsync(e => e.Id == c, ct).ConfigureAwait(false);
            history.Add(new HistoryEntryDto(copy.Id, copy.CreatedOn, copy.CreatedBy, JsonSerializer.SerializeToNode(ToDto(copy, [], [], [], []))!.AsObject()));
            cursor = copy.IdChange;
        }

        return history;
    }

    // ---------- Rules ----------

    /// <summary>The parent is a row of the same table, never the row itself or one below it.</summary>
    private static void CheckParent<T>(List<T> all, Func<T, Guid> idOf, Func<T, Guid?> parentOf, Guid? self, Guid? parent, string what)
    {
        if (parent is not { } p)
        {
            return;
        }

        if (all.All(r => idOf(r) != p))
        {
            throw ValidationException.For("idSup", $"The parent {what} does not exist.");
        }

        for (Guid? cursor = p; cursor is { } c; cursor = all.FirstOrDefault(r => idOf(r) == c) is { } row ? parentOf(row) : null)
        {
            if (c == self)
            {
                throw ValidationException.For("idSup", $"A {what} cannot be placed under itself or under one below it.");
            }
        }
    }

    /// <summary>The parent element, when it exists and is visible, and is neither the element itself nor one below it.</summary>
    private async Task<TElement?> ParentAsync(Guid? self, Guid? parentId, CancellationToken ct)
    {
        if (parentId is not { } p)
        {
            return null;
        }

        var parent = await VisibleAsync(await db.Set<TElement>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == p, ct).ConfigureAwait(false), ct).ConfigureAwait(false)
            ?? throw ValidationException.For("idSup", "The parent element does not exist.");
        // Up the tree one row at a time: only the chain above the parent is read.
        for (Guid? cursor = p; cursor is { } c; cursor = await db.Set<TElement>().Where(e => e.Id == c).Select(e => e.IdSup).FirstOrDefaultAsync(ct).ConfigureAwait(false))
        {
            if (c == self)
            {
                throw ValidationException.For("idSup", "An element cannot be placed under itself or under one below it.");
            }
        }

        return parent;
    }

    /// <summary>A model's table, schema-qualified, from the model (never from input).</summary>
    private string Qualified<T>()
    {
        var entity = db.Model.FindEntityType(typeof(T))!;
        return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
    }

    /// <summary>The elements whose name agrees with <paramref name="name"/> in a language both have (<see cref="SameName"/>, in SQL).</summary>
    private IQueryable<TElement> Named(TranslatedText name)
    {
        var sql = $"SELECT * FROM {Qualified<TElement>()} WHERE EXISTS (SELECT 1 FROM jsonb_each_text(name) a JOIN jsonb_each_text({{0}}::jsonb) b " +
            "ON lower(a.key) = lower(b.key) AND lower(btrim(a.value)) = lower(btrim(b.value)))";
        return db.Set<TElement>().FromSqlRaw(sql, JsonSerializer.Serialize(name.Values));
    }

    /// <summary>The elements whose code or name, in any language, contains <paramref name="term"/> (case aside; % and _ are literal).</summary>
    private IQueryable<TElement> Matching(string term)
    {
        var pattern = "%" + term.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var sql = $"SELECT * FROM {Qualified<TElement>()} WHERE code ILIKE {{0}} OR EXISTS (SELECT 1 FROM jsonb_each_text(name) n WHERE n.value ILIKE {{0}})";
        return db.Set<TElement>().FromSqlRaw(sql, pattern);
    }

    /// <summary>The next code of a type, within its range; counted in the type row without a history copy.</summary>
    private async Task<string?> NextCodeAsync(TType type, CancellationToken ct)
    {
        if (type.CodeStart is null && type.CodeEnd is null && type.CodeCurrent is null)
        {
            return null;
        }

        var table = Qualified<TType>();
        // Only the table's own name (from the model, never from input) is spliced in; the id is a parameter.
        var sql = $"UPDATE {table} SET code_current = COALESCE(code_current, code_start, 0) + 1 WHERE id = {{0}} " +
            "AND (code_end IS NULL OR COALESCE(code_current, code_start, 0) < code_end) RETURNING code_current - 1 AS \"Value\"";
        var numbers = await db.Database.SqlQueryRaw<int>(sql, type.Id).ToListAsync(ct).ConfigureAwait(false);
        if (numbers.Count == 0)
        {
            throw ValidationException.For("code", $"{ErrorCodes.CodeLimitError}: the type has used all its codes.");
        }

        return $"{type.Code}{numbers[0]}";
    }

    private async Task<Dictionary<Guid, JsonNode?>> CheckValuesAsync(Guid typeId, Dictionary<Guid, JsonNode?> input, CancellationToken ct)
    {
        var properties = await db.Set<TTypeInfo>().AsNoTracking().Where(p => p.TypeId == typeId).ToListAsync(ct).ConfigureAwait(false);
        var ids = properties.Select(p => p.Id).ToList();
        var choiceRows = await db.Set<TChoice>().AsNoTracking().Where(c => ids.Contains(c.TypeInfoId)).ToListAsync(ct).ConfigureAwait(false);
        var choices = choiceRows.ToLookup(c => c.TypeInfoId, c => c.Id);
        var errors = input.Keys.Where(k => properties.All(p => p.Id != k)).ToDictionary(k => $"values.{k}", _ => "is not a property of the type");
        var checkedValues = properties.ToDictionary(p => p.Id, p => PropertyValues.Check(p, input.GetValueOrDefault(p.Id), choices[p.Id].ToHashSet()));

        // A property shown for a choice of an activating list counts only while that list (itself shown) has the choice picked; otherwise it is
        // neither required nor kept.
        var byId = properties.ToDictionary(p => p.Id);
        var listOf = choiceRows.ToDictionary(c => c.Id, c => c.TypeInfoId);
        bool Shown(TTypeInfo p, int depth) => p.ShownForChoiceId is not { } choice
            || (depth < properties.Count && listOf.TryGetValue(choice, out var list) && Shown(byId[list], depth + 1)
                && checkedValues[list].Value is JsonArray picked && picked.Any(n => n?.GetValue<string>() == choice.ToString()));

        var values = new Dictionary<Guid, JsonNode?>();
        foreach (var property in properties)
        {
            var (value, error) = Shown(property, 0) ? checkedValues[property.Id] : (null, null);
            if (error is not null)
            {
                errors[$"values.{property.Id}"] = $"{property.Name.In(TranslatedText.DefaultLanguage)} {error}";
            }

            values[property.Id] = value;
        }

        return errors.Count == 0 ? values : throw new ValidationException(errors);
    }

    private async Task ValuesAsync(TElement element, Dictionary<Guid, JsonNode?> values, CancellationToken ct)
    {
        var existing = await db.Set<TInfo>().Where(v => v.ElementId == element.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var (propertyId, value) in values)
        {
            var row = existing.FirstOrDefault(v => v.TypeInfoId == propertyId);
            if (value is null)
            {
                if (row is not null)
                {
                    row.Active = false;
                }
            }
            else if (row is null)
            {
                db.Add(new TInfo { ElementId = element.Id, TypeInfoId = propertyId, Value = value, OwnerId = element.OwnerId });
            }
            else if (!JsonNode.DeepEquals(row.Value, value))
            {
                row.Value = value;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Makes a link table hold exactly <paramref name="wanted"/>: new links added, links no longer wanted switched off.</summary>
    private async Task LinkAsync<TLink>(System.Linq.Expressions.Expression<Func<TLink, bool>> of, Func<TLink, Guid> target, IEnumerable<Guid> wanted,
        Func<Guid, TLink> create, CancellationToken ct)
        where TLink : Row
    {
        var want = wanted.ToHashSet();
        var links = await db.Set<TLink>().Where(of).ToListAsync(ct).ConfigureAwait(false);
        foreach (var gone in links.Where(l => !want.Contains(target(l))))
        {
            gone.Active = false;
        }

        foreach (var id in want.Where(w => links.All(l => target(l) != w)))
        {
            db.Add(create(id));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> MaySeeAsync(IReadOnlyList<Guid> categories, IReadOnlyList<TAccess> access, CancellationToken ct)
    {
        foreach (var category in categories)
        {
            var rights = await visibility.RightsAsync(Table, category, ct).ConfigureAwait(false);
            if (rights.SeesAll || (rights.ReadOwn && access.Any(a => (a.UserId is not null && a.UserId == request.UserId)
                || (a.RoleId is not null && request.RoleIds.Contains(a.RoleId.Value)))))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> MayWriteAsync(IEnumerable<Guid> categories, CancellationToken ct)
    {
        foreach (var category in categories)
        {
            if ((await visibility.RightsAsync(Table, category, ct).ConfigureAwait(false)).Write)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The row when the request's node may see it (its owner is the node or one below, or it has no owner), else null. A cost centre that is
    /// itself the node or one below it is seen too, whoever made it (B15, the founder 2026-10-31): a bar arranges and adds the tables below it
    /// although the company made them. Only cost centres are nodes, so this changes nothing for the other registers.
    /// </summary>
    private async Task<T?> VisibleAsync<T>(T? row, CancellationToken ct)
        where T : Row
    {
        if (row is null)
        {
            return null;
        }

        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        return row.OwnerId is null || nodes.Contains(row.OwnerId.Value) || nodes.Contains(row.Id) ? row : null;
    }

    // ---------- Texts and DTOs ----------

    private static TranslatedText? Text(Dictionary<string, string>? values)
    {
        var kept = values?.Where(v => !string.IsNullOrWhiteSpace(v.Value)).ToDictionary(v => v.Key, v => v.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        return kept is { Count: > 0 } ? new TranslatedText { Values = kept } : null;
    }

    /// <summary>Two names are the same when they agree in a language both have.</summary>
    private static bool SameName(TranslatedText a, TranslatedText b) =>
        a.Values.Any(v => b.Values.TryGetValue(v.Key, out var other) && string.Equals(v.Value.Trim(), other.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool SameText(TranslatedText? a, TranslatedText? b) =>
        (a is null || a.Values.Count == 0) ? (b is null || b.Values.Count == 0) : b is not null && SameName(a, b);

    private static Dictionary<string, string> Values(TranslatedText? text) => text is null ? [] : new(text.Values, StringComparer.OrdinalIgnoreCase);

    private static CategoryDto ToDto(TCategory c) => new()
    {
        Id = c.Id, OwnerId = c.OwnerId, IdSup = c.IdSup, Active = c.Active, CreatedOn = c.CreatedOn, CreatedBy = c.CreatedBy, IsElement = c.IsElement, Code = c.Code,
        Name = Values(c.Name), Description = c.Description is null ? null : Values(c.Description), SortOrder = c.SortOrder,
    };

    private static ElementTypeDto ToDto(TType t, IEnumerable<TTypeCategory> links, IEnumerable<TTypeInfo> properties, ILookup<Guid, TChoice> choices) => new()
    {
        Id = t.Id, OwnerId = t.OwnerId, IdSup = t.IdSup, Active = t.Active, CreatedOn = t.CreatedOn, CreatedBy = t.CreatedBy, IsElement = t.IsElement, Code = t.Code,
        TracksSerial = t is Gamma.Erp.Models.Products.ProductType { TracksSerial: true },
        TracksLot = t is Gamma.Erp.Models.Products.ProductType { TracksLot: true },
        RefusesExpired = t is Gamma.Erp.Models.Products.ProductType { RefusesExpired: true },
        TakesBankStatements = t is Gamma.Erp.Models.CostCenters.CostCenterType { TakesBankStatements: true },
        IsProductionPoint = t is Gamma.Erp.Models.CostCenters.CostCenterType { IsProductionPoint: true },
        VatGroupId = (t as Gamma.Erp.Models.Products.ProductType)?.VatGroupId,
        FixedAsset = t is Gamma.Erp.Models.Products.ProductType { FixedAsset: true },
        CodeStart = t.CodeStart, CodeEnd = t.CodeEnd, CodeCurrent = t.CodeCurrent, Name = Values(t.Name), Description = t.Description is null ? null : Values(t.Description),
        SortOrder = t.SortOrder, CategoryIds = [.. links.Select(l => l.CategoryId)],
        Properties = [.. properties.Select(p => new TypeInfoDto
        {
            Id = p.Id, OwnerId = p.OwnerId, Active = p.Active, Name = Values(p.Name), Code = p.Code, Description = p.Description is null ? null : Values(p.Description), Required = p.Required,
            Kind = p.Kind, SortOrder = p.SortOrder, ElementsId = p.ElementsId, IsFile = p.IsFile, ShownForChoiceId = p.ShownForChoiceId,
            Choices = [.. choices[p.Id].Select(c => new TypeInfoChoiceDto { Id = c.Id, Active = c.Active, Name = Values(c.Name), Description = c.Description is null ? null : Values(c.Description) })],
        })],
    };

    private static ElementDto ToDto(TElement e, IEnumerable<Guid> categories, IEnumerable<TInfo> values, IEnumerable<Guid> users, IEnumerable<Guid> roles) => new()
    {
        Id = e.Id, OwnerId = e.OwnerId, IdSup = e.IdSup, Active = e.Active, CreatedOn = e.CreatedOn, CreatedBy = e.CreatedBy, TypeId = e.TypeId, Code = e.Code,
        Name = Values(e.Name), Description = e.Description is null ? null : Values(e.Description), IsComposite = e.IsComposite, DefaultVatGroupId = (e as Gamma.Erp.Models.Products.Product)?.VatGroupId, PaymentDays = (e as Gamma.Erp.Models.Partners.Partner)?.PaymentDays,
        Opportunity = e is Gamma.Erp.Models.Opportunities.Opportunity o ? new OpportunityFieldsDto(o.PartnerId, o.StageId, o.Amount, o.CurrencyId, o.ExpectedClose, o.OwnerUserId, o.LostReason) : null, CategoryIds = [.. categories],
        Values = values.ToDictionary(v => v.TypeInfoId, v => v.Value?.DeepClone()), AccessUserIds = [.. users], AccessRoleIds = [.. roles],
    };
}

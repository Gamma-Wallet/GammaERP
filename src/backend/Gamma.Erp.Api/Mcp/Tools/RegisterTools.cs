using System.ComponentModel;
using System.Text.Json;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Stock;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VatGroup = Gamma.Erp.Models.Payments.VatGroup;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>A property of a type as Claude gives it: a new one, or a change to the type's property with this code or name.</summary>
public sealed record TypeProperty(
    [property: Description("Its name.")] string Name,
    [property: Description("Optional: its code, the key its values are given by (e.g. TAX_NUMBER). The type's property with this code, or else this name, is the one changed.")] string? Code = null,
    [property: Description("Its kind: Text, Number, DateTime, Date, Photo, List, MultiList, Location or MultiText. Left out: the existing property's, or Text.")] string? Kind = null,
    [property: Description("Optional: whether every row of the type must have it.")] bool? Required = null,
    [property: Description("For a List or MultiList: all its choices, by name; existing choices left out are switched off. Left out: the choices stay as they are.")] IReadOnlyList<string>? Choices = null,
    [property: Description("True switches the property off.")] bool Remove = false);

/// <summary>
/// The registers as their screens keep them (partners, products, accounts, units, cost centres): their set-up (categories, types and their
/// properties) and their rows, added, changed or switched off. Each goes through the register's own service, with the user's rights on its
/// categories, as the screens do; the set-up needs the register's Set up screen.
/// </summary>
[McpServerToolType]
public sealed class RegisterTools(ErpDbContext db, RequestContext request, Visibility visibility, ProductUnitService productUnits)
{
    private const string RegisterHelp = "The register: partners (clients and suppliers), products (products and services), accounts (the chart of accounts), units, or cost-centres.";
    private const string LanguageHelp = "Optional: the language of the name and description given (e.g. it); English when left out.";

    [McpServerTool(Name = "register_setup", Title = "A register's categories, types and properties", ReadOnly = true)]
    [Description("Reads a register's set-up: its categories (holdsRows: rows can be put in it), its types with their categories and properties (code, kind, required, choices), and for products the VAT groups and units there are (and a type's units when one type is asked). Read it before save_register_row, to know the type, the category and the property codes to give.")]
    public Task<object> RegisterSetupAsync([Description(RegisterHelp)] string register,
        [Description("Optional: only this type (id, code or name).")] string? type = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            var r = Registers.Of(register, db, request, visibility);
            var categories = (await r.Rows.CategoriesAsync(ct).ConfigureAwait(false)).Where(c => c.Active).ToList();
            var types = (await r.Rows.TypesAsync(ct).ConfigureAwait(false)).Where(t => t.Active).ToList();
            if (!string.IsNullOrWhiteSpace(type))
            {
                types = [Registers.Type(types, type, r.What)];
            }

            var categoryNames = categories.Where(c => c.Id is not null).ToDictionary(c => c.Id!.Value, c => c.Code ?? Registers.Name(c.Name));
            var products = r.Key == "products";
            var vatGroups = products ? await VatGroupsAsync(ct).ConfigureAwait(false) : [];
            var vatNames = vatGroups.ToDictionary(v => v.Id, v => v.Code ?? v.Name?.In(Registers.English));
            object? typeUnits = null;
            if (products && types.Count == 1 && types[0].IsElement)
            {
                typeUnits = (await productUnits.TypeAsync(types[0].Id!.Value, Registers.English, ct).ConfigureAwait(false))?.Select(u => new { u.UnitId, u.Code, u.Name });
            }

            object? unitList = null;
            if (products)
            {
                var page = await Registers.Units(db, request, visibility).LookupAsync(null, null, null, Registers.English, 1, Registers.MaxPage, ct).ConfigureAwait(false);
                unitList = page.Items.Select(u => new { u.Id, u.Code, u.Name });
            }

            return new
            {
                register = r.Key,
                categories = categories.Select(c => new { c.Id, c.Code, name = Registers.Name(c.Name), parentId = c.IdSup, holdsRows = c.IsElement }),
                types = types.Select(t => new
                {
                    t.Id, t.Code, name = Registers.Name(t.Name), parentId = t.IdSup, isFolder = !t.IsElement,
                    categories = t.CategoryIds.Select(id => categoryNames.GetValueOrDefault(id)).Where(n => n is not null),
                    vatGroup = products && t.VatGroupId is { } v ? vatNames.GetValueOrDefault(v) : null,
                    tracksLot = products ? t.TracksLot : (bool?)null, tracksSerial = products ? t.TracksSerial : (bool?)null, fixedAsset = products ? t.FixedAsset : (bool?)null,
                    takesBankStatements = r.Key == "cost-centres" ? t.TakesBankStatements : (bool?)null, isProductionPoint = r.Key == "cost-centres" ? t.IsProductionPoint : (bool?)null,
                    properties = Registers.Properties(t),
                }),
                typeUnits,
                vatGroups = products ? vatGroups.Select(v => new { v.Id, v.Code, name = v.Name?.In(Registers.English), v.Rate }) : null,
                units = unitList,
            };
        });

    [McpServerTool(Name = "save_register_row", Title = "Add or change a row of a register", ReadOnly = false, Destructive = false)]
    [Description("Adds or changes a row of a register, as its screen does: a client or supplier (partners), a product or service, an account, a unit or a cost centre. A new row needs its name and type (and the category when the type is in several); a change names the row and only what is given changes. Property values go by their codes (register_setup lists them; a type's required ones must be there). Partners also take paymentDays; products the default VAT group, whether it is made of others (composite) and its units in order, the first the default. Show the user what will be saved and get an explicit yes; then confirm=true. Returns the saved row.")]
    public Task<object> SaveRegisterRowAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description(RegisterHelp)] string register,
        [Description("To change a row: its id or exact code. Left out: a new row.")] string? row = null,
        [Description("Its name (required for a new row).")] string? name = null,
        [Description("Its type: id, code or name (required for a new row).")] string? type = null,
        [Description("Optional: its categories (codes, names or ids); a new row takes its type's category when the type is in only one.")] IReadOnlyList<string>? categories = null,
        [Description("Optional: its code; a new row takes its type's next code when left out.")] string? code = null,
        [Description("Optional: the row it sits under (id or exact code; for a cost centre, the company or place it is under); \"none\" puts it at the top.")] string? parent = null,
        [Description("Optional: its description.")] string? description = null,
        [Description("Optional: its property values, property code → value (an empty value clears it).")] Dictionary<string, JsonElement>? properties = null,
        [Description("Partners only: the days it has to pay.")] int? paymentDays = null,
        [Description("Products only: the VAT group its lines start with (id, code or name); \"none\" takes its type's.")] string? vatGroup = null,
        [Description("Products only: its units (ids, codes or names) in order, the first the default; the whole list.")] IReadOnlyList<string>? units = null,
        [Description("Products only: true when it is made of other products.")] bool? composite = null,
        [Description(LanguageHelp)] string? language = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the row to save (register, type, category, name, code, values)");
            Erp.NeedsPlace(request);
            var r = Registers.Of(register, db, request, visibility);
            Only(r, "partners", "paymentDays", paymentDays is not null);
            Only(r, "products", "vatGroup", vatGroup is not null);
            Only(r, "products", "units", units is not null);
            Only(r, "products", "composite", composite is not null);
            var existing = row is null ? null : await r.Rows.ElementAsync(await RowIdAsync(r, row, ct).ConfigureAwait(false), ct).ConfigureAwait(false)
                ?? throw new McpException($"There is no such {r.What} that you may see.");
            var types = await r.Rows.TypesAsync(ct).ConfigureAwait(false);
            var typeDto = !string.IsNullOrWhiteSpace(type) ? Registers.Type(types, type, r.What)
                : existing is not null ? types.FirstOrDefault(t => t.Id == existing.TypeId) ?? throw new McpException($"The {r.What}'s type is not found.")
                : throw new McpException($"Give the {r.What}'s type (register_setup lists them).");
            var categoryList = await r.Rows.CategoriesAsync(ct).ConfigureAwait(false);
            IReadOnlyList<Guid> categoryIds = categories is { Count: > 0 } ? [.. categories.Select(c => Registers.Category(categoryList, c, r.What)!.Value).Distinct()]
                : existing?.CategoryIds ?? (typeDto.CategoryIds.Count == 1 ? [typeDto.CategoryIds[0]] : throw new McpException("The type is in several categories (or none): give the category."));
            if (existing is null && string.IsNullOrWhiteSpace(name))
            {
                throw new McpException($"Give the new {r.What}'s name.");
            }

            var lang = string.IsNullOrWhiteSpace(language) ? Registers.English : language.Trim().ToLowerInvariant();
            var parentId = parent is null ? existing?.IdSup
                : IsNone(parent) ? null : await RowIdAsync(r, parent, ct).ConfigureAwait(false);
            var vatId = vatGroup is null ? existing?.DefaultVatGroupId : IsNone(vatGroup) ? null : await VatGroupAsync(vatGroup, ct).ConfigureAwait(false);
            var dto = (existing ?? new ElementDto { TypeId = typeDto.Id!.Value }) with
            {
                TypeId = typeDto.Id!.Value,
                Code = string.IsNullOrWhiteSpace(code) ? existing?.Code : code.Trim(),
                Name = Text(existing?.Name, name, lang) ?? [],
                Description = Text(existing?.Description, description, lang),
                IdSup = parentId,
                CategoryIds = categoryIds,
                Values = Registers.Values(typeDto, properties, existing?.Values),
                PaymentDays = paymentDays ?? existing?.PaymentDays,
                DefaultVatGroupId = vatId,
                IsComposite = composite ?? existing?.IsComposite ?? false,
            };

            var unitIds = new List<Guid>();
            foreach (var unit in units ?? [])
            {
                unitIds.Add(await Registers.ElementAsync(s => Registers.Units(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), unit, "unit")
                    .ConfigureAwait(false));
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var saved = await r.Rows.SaveElementAsync(dto, ct).ConfigureAwait(false);
            object? savedUnits = null;
            if (units is not null)
            {
                savedUnits = (await productUnits.SaveProductAsync(saved.Id!.Value, unitIds, Registers.English, ct).ConfigureAwait(false)).Units.Select(u => new { u.UnitId, u.Code, u.Name });
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            var vatName = saved.DefaultVatGroupId is { } v ? (await VatGroupsAsync(ct).ConfigureAwait(false)).FirstOrDefault(g => g.Id == v) is { } g ? g.Code ?? g.Name?.In(Registers.English) : null : null;
            return new
            {
                saved.Id, saved.Code, name = Registers.Name(saved.Name), type = Registers.Name(typeDto.Name), parentId = saved.IdSup,
                categories = saved.CategoryIds.Select(id => categoryList.FirstOrDefault(c => c.Id == id) is { } c ? c.Code ?? Registers.Name(c.Name) : id.ToString()),
                values = typeDto.Properties.Where(p => p.Active && p.Id is not null && saved.Values.ContainsKey(p.Id.Value))
                    .ToDictionary(p => p.Code ?? Registers.Name(p.Name) ?? p.Id.ToString()!, p => saved.Values[p.Id!.Value]),
                paymentDays = r.Key == "partners" ? saved.PaymentDays : null,
                vatGroup = vatName, composite = r.Key == "products" ? saved.IsComposite : (bool?)null, units = savedUnits,
            };
        });

    [McpServerTool(Name = "switch_off_register_row", Title = "Switch off a row of a register", ReadOnly = false, Destructive = true)]
    [Description("Switches off a row of a register (a partner, product, account, unit or cost centre), as the screen's delete does: nothing is deleted, history stays, and it is no longer offered. Refused while active rows sit under it, and for the company's general client. Show the user the row and get an explicit yes; then confirm=true.")]
    public Task<object> SwitchOffRegisterRowAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description(RegisterHelp)] string register,
        [Description("The row: its id or exact code.")] string row, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the row to switch off");
            Erp.NeedsPlace(request);
            var r = Registers.Of(register, db, request, visibility);
            var id = await RowIdAsync(r, row, ct).ConfigureAwait(false);
            var element = await r.Rows.ElementAsync(id, ct).ConfigureAwait(false) ?? throw new McpException($"There is no such {r.What} that you may see.");
            await r.Rows.DeleteElementAsync(id, ct).ConfigureAwait(false);
            return new { switchedOff = true, element.Id, element.Code, name = Registers.Name(element.Name) };
        });

    [McpServerTool(Name = "save_register_category", Title = "Add or change a register's category", ReadOnly = false, Destructive = false)]
    [Description("Adds or changes a category of a register, as its Set up screen does (e.g. a \"Gelato\" category of products). A category that holds rows (holdsRows) takes rows; otherwise it only groups other categories. Needs the register's Set up screen. Show the user what will be saved and get an explicit yes; then confirm=true.")]
    public Task<object> SaveRegisterCategoryAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description(RegisterHelp)] string register,
        [Description("To change a category: its id, code or name. Left out: a new one.")] string? category = null,
        [Description("Its name (required for a new one).")] string? name = null,
        [Description("Optional: its code.")] string? code = null,
        [Description("Optional: the category it sits under (id, code or name); \"none\" puts it at the top.")] string? parent = null,
        [Description("Optional: true when rows can be put in it (a new one: true).")] bool? holdsRows = null,
        [Description("True switches the category off.")] bool switchOff = false,
        [Description(LanguageHelp)] string? language = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the category to save");
            Erp.NeedsPlace(request);
            var r = Registers.Of(register, db, request, visibility);
            await Registers.Screen(visibility, ["setup"], [r.Route], ct).ConfigureAwait(false);
            var all = await r.Rows.CategoriesAsync(ct).ConfigureAwait(false);
            var existing = category is null ? null : all.First(c => c.Id == Registers.Category(all, category, r.What));
            if (existing is null && string.IsNullOrWhiteSpace(name))
            {
                throw new McpException("Give the new category's name.");
            }

            var lang = string.IsNullOrWhiteSpace(language) ? Registers.English : language.Trim().ToLowerInvariant();
            var saved = await r.Rows.SaveCategoryAsync((existing ?? new CategoryDto()) with
            {
                Name = Text(existing?.Name, name, lang) ?? [],
                Code = string.IsNullOrWhiteSpace(code) ? existing?.Code : code.Trim(),
                IdSup = parent is null ? existing?.IdSup : IsNone(parent) ? null : Registers.Category(all, parent, r.What),
                IsElement = holdsRows ?? existing?.IsElement ?? true,
                Active = !switchOff,
            }, ct).ConfigureAwait(false);
            return new { saved.Id, saved.Code, name = Registers.Name(saved.Name), parentId = saved.IdSup, holdsRows = saved.IsElement, saved.Active };
        });

    [McpServerTool(Name = "save_register_type", Title = "Add or change a register's type", ReadOnly = false, Destructive = false)]
    [Description("Adds or changes a type of a register, as its Set up screen does: name, code, the type above it, its categories, whether it is a folder of types, its properties (each given one is added, or changes the type's property with that code or name; the others stay), and its code range. Product types also take the VAT group, lots, serial numbers, fixed asset and their units; cost-centre types whether they take bank statements or are production points. Needs the register's Set up screen (a product type's units: an administrator). Show the user what will be saved and get an explicit yes; then confirm=true.")]
    public Task<object> SaveRegisterTypeAsync(
        [Description("True only after the user said yes.")] bool confirm,
        [Description(RegisterHelp)] string register,
        [Description("To change a type: its id, code or name. Left out: a new one.")] string? type = null,
        [Description("Its name (required for a new one).")] string? name = null,
        [Description("Optional: its code.")] string? code = null,
        [Description("Optional: the type it sits under (id, code or name); \"none\" puts it at the top.")] string? parent = null,
        [Description("Its categories (codes, names or ids); required for a new one.")] IReadOnlyList<string>? categories = null,
        [Description("Optional: true when it only groups other types (a new one: false).")] bool? isFolder = null,
        [Description("Optional: properties to add or change.")] IReadOnlyList<TypeProperty>? properties = null,
        [Description("Optional: the first code its rows are numbered with.")] int? codeStart = null,
        [Description("Optional: the last code its rows are numbered with.")] int? codeEnd = null,
        [Description("Product types: the VAT group its products start with (id, code or name); \"none\" for none.")] string? vatGroup = null,
        [Description("Product types: its products move lot by lot.")] bool? tracksLot = null,
        [Description("Product types: each unit has a serial number.")] bool? tracksSerial = null,
        [Description("Product types: its products are fixed assets.")] bool? fixedAsset = null,
        [Description("Product types: its units (ids, codes or names) in order, the first the default; the whole list.")] IReadOnlyList<string>? units = null,
        [Description("Cost-centre types: its cost centres are bank accounts that take statements.")] bool? takesBankStatements = null,
        [Description("Cost-centre types: its cost centres are production points (kitchen, bar).")] bool? isProductionPoint = null,
        [Description("True switches the type off.")] bool switchOff = false,
        [Description(LanguageHelp)] string? language = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the type to save, with its properties");
            Erp.NeedsPlace(request);
            var r = Registers.Of(register, db, request, visibility);
            foreach (var (field, given) in new[] { ("vatGroup", vatGroup is not null), ("tracksLot", tracksLot is not null), ("tracksSerial", tracksSerial is not null),
                ("fixedAsset", fixedAsset is not null), ("units", units is not null) })
            {
                Only(r, "products", field, given);
            }

            Only(r, "cost-centres", "takesBankStatements", takesBankStatements is not null);
            Only(r, "cost-centres", "isProductionPoint", isProductionPoint is not null);
            await Registers.Screen(visibility, ["setup"], [r.Route], ct).ConfigureAwait(false);
            var types = await r.Rows.TypesAsync(ct).ConfigureAwait(false);
            var existing = type is null ? null : Registers.Type(types, type, r.What);
            if (existing is null && (string.IsNullOrWhiteSpace(name) || categories is not { Count: > 0 }))
            {
                throw new McpException("A new type needs its name and its categories.");
            }

            var lang = string.IsNullOrWhiteSpace(language) ? Registers.English : language.Trim().ToLowerInvariant();
            var categoryList = await r.Rows.CategoriesAsync(ct).ConfigureAwait(false);
            var dto = (existing ?? new ElementTypeDto()) with
            {
                Name = Text(existing?.Name, name, lang) ?? [],
                Code = string.IsNullOrWhiteSpace(code) ? existing?.Code : code.Trim(),
                IdSup = parent is null ? existing?.IdSup : IsNone(parent) ? null : Registers.Type(types, parent, r.What).Id,
                CategoryIds = categories is { Count: > 0 } ? [.. categories.Select(c => Registers.Category(categoryList, c, r.What)!.Value).Distinct()] : existing!.CategoryIds,
                IsElement = isFolder is { } folder ? !folder : existing?.IsElement ?? true,
                Properties = Properties(existing?.Properties ?? [], properties, lang),
                CodeStart = codeStart ?? existing?.CodeStart,
                CodeEnd = codeEnd ?? existing?.CodeEnd,
                CodeCurrent = codeStart is not null && existing?.CodeCurrent is null ? codeStart : existing?.CodeCurrent,
                VatGroupId = vatGroup is null ? existing?.VatGroupId : IsNone(vatGroup) ? null : await VatGroupAsync(vatGroup, ct).ConfigureAwait(false),
                TracksLot = tracksLot ?? existing?.TracksLot ?? false,
                TracksSerial = tracksSerial ?? existing?.TracksSerial ?? false,
                FixedAsset = fixedAsset ?? existing?.FixedAsset ?? false,
                TakesBankStatements = takesBankStatements ?? existing?.TakesBankStatements ?? false,
                IsProductionPoint = isProductionPoint ?? existing?.IsProductionPoint ?? false,
                Active = !switchOff,
            };

            var unitIds = new List<Guid>();
            foreach (var unit in units ?? [])
            {
                unitIds.Add(await Registers.ElementAsync(s => Registers.Units(db, request, visibility).LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), unit, "unit")
                    .ConfigureAwait(false));
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var saved = await r.Rows.SaveTypeAsync(dto, ct).ConfigureAwait(false);
            object? savedUnits = null;
            if (units is not null)
            {
                savedUnits = (await productUnits.SaveTypeAsync(saved.Id!.Value, unitIds, Registers.English, ct).ConfigureAwait(false)).Select(u => new { u.UnitId, u.Code, u.Name });
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new
            {
                saved.Id, saved.Code, name = Registers.Name(saved.Name), parentId = saved.IdSup, isFolder = !saved.IsElement, saved.Active,
                categories = saved.CategoryIds.Select(id => categoryList.FirstOrDefault(c => c.Id == id) is { } c ? c.Code ?? Registers.Name(c.Name) : id.ToString()),
                properties = Registers.Properties(saved), units = savedUnits,
            };
        });

    /// <summary>The type's properties as they will be sent: the active ones it has, with the given ones added or changed (sent whole, as the screen sends them).</summary>
    private static List<TypeInfoDto> Properties(IReadOnlyList<TypeInfoDto> existing, IReadOnlyList<TypeProperty>? given, string language)
    {
        var list = existing.Where(p => p.Active).ToList();
        foreach (var wanted in given ?? [])
        {
            var i = list.FindIndex(p => !string.IsNullOrWhiteSpace(wanted.Code) && string.Equals(p.Code, wanted.Code.Trim(), StringComparison.OrdinalIgnoreCase));
            if (i < 0)
            {
                i = list.FindIndex(p => string.Equals(Registers.Name(p.Name), wanted.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (wanted.Remove)
            {
                if (i < 0)
                {
                    throw new McpException($"The type has no property {wanted.Code ?? wanted.Name} to switch off.");
                }

                list.RemoveAt(i);
                continue;
            }

            var old = i < 0 ? null : list[i];
            PropertyKind kind;
            if (string.IsNullOrWhiteSpace(wanted.Kind))
            {
                kind = old?.Kind ?? PropertyKind.Text;
            }
            else if (!Enum.TryParse(wanted.Kind.Trim(), true, out kind) || !Enum.IsDefined(kind))
            {
                throw new McpException($"Unknown kind \"{wanted.Kind}\". Kinds: {string.Join(", ", Enum.GetNames<PropertyKind>())}.");
            }

            if (kind == PropertyKind.Lookup && old?.Kind != PropertyKind.Lookup)
            {
                throw new McpException("A Lookup property names the register it picks from: add it on the register's Set up screen.");
            }

            var choices = old?.Choices.Where(c => c.Active).ToList() ?? [];
            if (wanted.Choices is not null)
            {
                choices = [.. wanted.Choices.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(c => choices.FirstOrDefault(o => string.Equals(Registers.Name(o.Name), c, StringComparison.OrdinalIgnoreCase))
                        ?? new TypeInfoChoiceDto { Name = new() { [language] = c } })];
            }

            var property = (old ?? new TypeInfoDto()) with
            {
                Name = Text(old?.Name, wanted.Name, language) ?? [],
                Code = string.IsNullOrWhiteSpace(wanted.Code) ? old?.Code : wanted.Code.Trim(),
                Kind = kind,
                Required = wanted.Required ?? old?.Required ?? false,
                Choices = choices,
            };
            if (i < 0)
            {
                list.Add(property);
            }
            else
            {
                list[i] = property;
            }
        }

        return list;
    }

    private static async Task<Guid> RowIdAsync(Register r, string given, CancellationToken ct) =>
        await Registers.ElementAsync(s => r.Rows.LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), given, r.What).ConfigureAwait(false);

    /// <summary>The VAT groups of the company the user works in (and the shared ones).</summary>
    private async Task<List<VatGroup>> VatGroupsAsync(CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        return await db.Set<VatGroup>().AsNoTracking().Where(v => v.Active && (v.OwnerId == company || v.OwnerId == null)).OrderBy(v => v.Code).ThenByDescending(v => v.Rate)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<Guid> VatGroupAsync(string given, CancellationToken ct)
    {
        var key = given.Trim();
        var groups = await VatGroupsAsync(ct).ConfigureAwait(false);
        var found = groups.Where(v => Registers.Is(v.Id, key) || string.Equals(v.Code, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(v.Name?.In(Registers.English), key, StringComparison.OrdinalIgnoreCase)).ToList();
        return found.Count switch
        {
            1 => found[0].Id,
            0 => throw new McpException($"There is no VAT group \"{key}\". Known: {string.Join(", ", groups.Select(v => $"{v.Code} ({v.Rate}%)"))}."),
            _ => throw new McpException($"Several VAT groups match \"{key}\": give its id."),
        };
    }

    /// <summary>A text in several languages with one language's value set (or kept as it is when none is given); null when there is none.</summary>
    private static Dictionary<string, string>? Text(Dictionary<string, string>? existing, string? given, string language)
    {
        if (string.IsNullOrWhiteSpace(given))
        {
            return existing;
        }

        var text = existing is null ? new Dictionary<string, string>() : new Dictionary<string, string>(existing);
        text[language] = given.Trim();
        return text;
    }

    private static bool IsNone(string given) => string.Equals(given.Trim(), "none", StringComparison.OrdinalIgnoreCase) || given.Trim().Length == 0;

    private static void Only(Register r, string register, string field, bool given)
    {
        if (given && r.Key != register)
        {
            throw new McpException($"{field} is for {register} only.");
        }
    }
}

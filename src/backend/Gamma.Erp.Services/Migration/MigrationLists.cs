using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Currencies;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Stock;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Migration;

/// <summary>A column of a list: its heading, whether a new row needs it, and what it takes.</summary>
public sealed record MigrationColumn(string Header, bool Required, string Help);

/// <summary>A list a migration step reads and writes. A row is its cells by heading, as text.</summary>
public abstract class MigrationList
{
    protected const string Separator = ";";

    public abstract Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct);

    public abstract Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct);

    /// <summary>Whether the step holds data, for the steps that wait for it.</summary>
    public abstract Task<int> CountAsync(CancellationToken ct);

    /// <summary>Reads what the rows are matched against; once per import.</summary>
    public virtual Task PrepareAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Saves one row through the entity's own rules; true when it added one, false when it changed one.</summary>
    public abstract Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct);

    /// <summary>Runs once every row is in, for a list that saves the file as a whole.</summary>
    public virtual Task FinishAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>The document the file posted, kept with the run.</summary>
    public virtual Guid? PostedDocument => null;

    /// <summary>What an Opening list's account stands at now.</summary>
    public virtual Task<decimal?> AfterAsync(CancellationToken ct) => Task.FromResult<decimal?>(null);

    protected static string? Cell(IReadOnlyDictionary<string, string> row, string header) =>
        row.TryGetValue(header, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    protected static string Needed(IReadOnlyDictionary<string, string> row, string header) =>
        Cell(row, header) ?? throw ValidationException.For(header, "is required.");

    protected static decimal Number(IReadOnlyDictionary<string, string> row, string header) =>
        decimal.TryParse(Needed(row, header), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : throw ValidationException.For(header, "is not a number.");

    protected static string Allowed(IEnumerable<string?> values) => string.Join(", ", values.OfType<string>().Distinct(StringComparer.Ordinal));

    protected static string English(Dictionary<string, string>? text) =>
        text is null ? string.Empty : text.GetValueOrDefault(TranslatedText.DefaultLanguage) ?? text.Values.FirstOrDefault() ?? string.Empty;

    /// <summary>The heading of a name in the company's own language; none when that is English.</summary>
    protected static string? LocalHeader(string language) => language == TranslatedText.DefaultLanguage ? null : $"Name ({language})";

    /// <summary>A row's name, in English and the company's language: a new row needs one of them and the other follows it, a changed one keeps what it doesn't give.</summary>
    protected static Dictionary<string, string> Names(IReadOnlyDictionary<string, string> row, Dictionary<string, string>? existing, string language)
    {
        var names = existing is null ? new Dictionary<string, string>() : new Dictionary<string, string>(existing);
        foreach (var (header, key) in new[] { ("Name", TranslatedText.DefaultLanguage), (LocalHeader(language), language) })
        {
            if (header is not null && Cell(row, header) is { } text)
            {
                names[key] = text;
            }
        }

        if (names.Count == 0)
        {
            throw ValidationException.For("Name", "is required.");
        }

        return new TranslatedText { Values = new(names, StringComparer.OrdinalIgnoreCase) }.Completed(language).Values;
    }
}

/// <summary>
/// The elements of an entity: columns of their own, then one column per property the entity's types configure. On the chart (<c>chart</c> given) names stand in the
/// country's language, English is optional, and an account can take a new code: the delegate checks and carries out what that touches.
/// </summary>
public sealed class ElementList(IEntityRows entity, ErpDbContext db, Visibility visibility, string language, Func<Guid?>? defaultParent = null, ProductUnitService? units = null, IEntityRows? unitRows = null,
    Func<string, string, CancellationToken, Task>? chart = null) : MigrationList
{
    private const string Code = nameof(Code), Name = nameof(Name), Type = nameof(Type), Category = nameof(Category), Parent = nameof(Parent), Description = nameof(Description), Units = nameof(Units), NewCode = "New code", Vat = "VAT group";

    private static readonly PropertyKind[] Shown = [PropertyKind.Text, PropertyKind.Number, PropertyKind.Date, PropertyKind.List, PropertyKind.MultiList, PropertyKind.ActivatingList, PropertyKind.Lookup];

    private List<ElementTypeDto> _types = [];
    private List<CategoryDto> _categories = [];
    private Dictionary<string, ElementDto> _byCode = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<Guid, string?> _codes = [];
    private Dictionary<string, Guid> _unitIds = new(StringComparer.OrdinalIgnoreCase);
    private List<VatGroup> _vat = [];
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public override Task<int> CountAsync(CancellationToken ct) => entity.CountElementsAsync(ct);

    public override async Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct)
    {
        var types = await entity.TypesAsync(ct).ConfigureAwait(false);
        var categories = await entity.CategoriesAsync(ct).ConfigureAwait(false);
        var columns = new List<MigrationColumn>
        {
            new(Code, false, "Rows are matched by code: an existing code is updated. Empty: the next code of the type, or the row of the same name if its type gives no codes."),
            new(Name, chart is null, "The name in English."),
            new(Type, true, Allowed(types.Select(t => t.Code))),
            new(Category, true, $"One or several, separated by {Separator}: {Allowed(categories.Where(c => c.IsElement).Select(c => c.Code))}"),
            new(Parent, false, "The code of the row above it."),
            new(Description, false, "A description."),
        };
        if (LocalHeader(language) is { } local)
        {
            columns.Insert(2, new(local, true, $"The name in the company's language ({language})."));
        }

        if (chart is not null)
        {
            columns.Add(new(NewCode, false, "The account's new number: posting groups and balances follow it. Refused if it would change the account's statement line."));
        }

        if (units is not null)
        {
            columns.Add(new(Units, false, $"Unit codes, separated by {Separator}. Empty: the units of its type."));
            columns.Add(new(Vat, false, $"{Allowed((await VatLookups.GroupsAsync(db, visibility, ct).ConfigureAwait(false)).Select(VatLookups.NameOf))}. Empty: its type's."));
        }

        foreach (var property in Properties(types).GroupBy(Header).Select(g => g.First()))
        {
            var choices = property.Choices.Select(c => English(c.Name)).ToList();
            columns.Add(new(Header(property), false, property.Kind == PropertyKind.Lookup ? "A code." : choices.Count > 0 ? Allowed(choices) : property.Kind.ToString()));
        }

        return columns;
    }

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        await PrepareAsync(ct).ConfigureAwait(false);
        var typeOf = _types.ToDictionary(t => t.Id!.Value);
        var categoryOf = _categories.ToDictionary(c => c.Id!.Value, c => c.Code);
        var unitsOf = await UnitsOfAsync(ct).ConfigureAwait(false);
        var codes = await LookupCodesAsync(_byCode.Values.Concat(Uncoded), ct).ConfigureAwait(false);
        return [.. _byCode.Values.Concat(Uncoded).OrderBy(e => e.Code, StringComparer.Ordinal).Select(e =>
        {
            var type = typeOf.GetValueOrDefault(e.TypeId);
            var row = new Dictionary<string, object?>
            {
                [Code] = e.Code, [Name] = chart is null ? English(e.Name) : e.Name.GetValueOrDefault(TranslatedText.DefaultLanguage), [Type] = type?.Code, [Category] = string.Join(Separator, e.CategoryIds.Select(c => categoryOf.GetValueOrDefault(c))),
                [Parent] = e.IdSup is { } up ? _codes.GetValueOrDefault(up) : null, [Description] = English(e.Description),
            };
            if (LocalHeader(language) is { } local)
            {
                row[local] = e.Name.GetValueOrDefault(language);
            }

            if (units is not null)
            {
                row[Units] = unitsOf.GetValueOrDefault(e.Id!.Value);
                row[Vat] = e.DefaultVatGroupId is { } group ? _vat.FirstOrDefault(v => v.Id == group) is { } found ? VatLookups.NameOf(found) : null : null;
            }

            foreach (var property in type?.Properties.Where(p => Shown.Contains(p.Kind)) ?? [])
            {
                var value = e.Values.GetValueOrDefault(property.Id!.Value);
                row[Header(property)] = property.Kind == PropertyKind.Lookup ? codes.GetValueOrDefault(value?.GetValue<string>() ?? string.Empty) : Text(property, value);
            }

            return row;
        })];
    }

    private List<ElementDto> Uncoded { get; set; } = [];

    public override async Task PrepareAsync(CancellationToken ct)
    {
        _types = [.. await entity.TypesAsync(ct).ConfigureAwait(false)];
        _categories = [.. await entity.CategoriesAsync(ct).ConfigureAwait(false)];
        var all = await entity.AllElementsAsync(ct).ConfigureAwait(false);
        _byCode = new(all.Where(e => e.Code is not null).GroupBy(e => e.Code!, StringComparer.OrdinalIgnoreCase).Select(g => KeyValuePair.Create(g.Key, g.First())), StringComparer.OrdinalIgnoreCase);
        Uncoded = [.. all.Where(e => e.Code is null)];
        _codes = all.ToDictionary(e => e.Id!.Value, e => e.Code);
        if (unitRows is not null)
        {
            _vat = await VatLookups.GroupsAsync(db, visibility, ct).ConfigureAwait(false);
            _unitIds = new((await unitRows.AllElementsAsync(ct).ConfigureAwait(false)).Where(u => u.Code is not null).GroupBy(u => u.Code!, StringComparer.OrdinalIgnoreCase)
                .Select(g => KeyValuePair.Create(g.Key, g.First().Id!.Value)), StringComparer.OrdinalIgnoreCase);
        }
    }

    public override async Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var code = Cell(row, Code);
        if (code is not null && !_seen.Add(code))
        {
            throw ValidationException.For(Code, "appears twice in the file.");
        }

        // A row whose type gives no codes has none to be matched by, so its name stands for it.
        var existing = code is not null ? _byCode.GetValueOrDefault(code) : Uncoded.FirstOrDefault(e => string.Equals(English(e.Name), Cell(row, Name), StringComparison.OrdinalIgnoreCase));
        var type = Cell(row, Type) is { } typeCode
            ? _types.FirstOrDefault(t => string.Equals(t.Code, typeCode, StringComparison.OrdinalIgnoreCase)) ?? throw ValidationException.For(Type, $"There is no type {typeCode}.")
            : _types.FirstOrDefault(t => t.Id == existing?.TypeId) ?? throw ValidationException.For(Type, "is required.");
        var categories = (Cell(row, Category)?.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? []).Select(c =>
            _categories.FirstOrDefault(x => string.Equals(x.Code, c, StringComparison.OrdinalIgnoreCase))?.Id ?? throw ValidationException.For(Category, $"There is no category {c}.")).ToList();
        if (categories.Count == 0 && existing is null)
        {
            throw ValidationException.For(Category, "is required.");
        }

        Guid? parent = Cell(row, Parent) is { } parentCode
            ? _byCode.GetValueOrDefault(parentCode)?.Id ?? throw ValidationException.For(Parent, $"There is no row with the code {parentCode} above this one in the system or the file.")
            : existing is null ? defaultParent?.Invoke() : existing.IdSup;
        var values = existing is null ? [] : new Dictionary<Guid, JsonNode?>(existing.Values);
        foreach (var column in row.Keys.Except(Own, StringComparer.Ordinal).Where(c => c != LocalHeader(language) && Cell(row, c) is not null))
        {
            var property = type.Properties.FirstOrDefault(p => Shown.Contains(p.Kind) && Header(p) == column)
                ?? throw ValidationException.For(column, $"is not a property of the type {type.Code}.");
            values[property.Id!.Value] = property.Kind == PropertyKind.Lookup
                ? JsonValue.Create((await LookupAsync(property, Cell(row, column)!, column, ct).ConfigureAwait(false)).ToString())
                : Json(property, Cell(row, column)!, column);
        }

        var name = Names(row, existing?.Name, language);
        var description = Cell(row, Description) is { } about ? new Dictionary<string, string> { [TranslatedText.DefaultLanguage] = about } : existing?.Description;
        var renamed = chart is null ? null : Cell(row, NewCode);
        if (renamed is not null && !string.Equals(renamed, code, StringComparison.OrdinalIgnoreCase))
        {
            if (existing is null)
            {
                throw ValidationException.For(NewCode, "Only an existing account takes a new code.");
            }

            if (_byCode.ContainsKey(renamed))
            {
                throw ValidationException.For(NewCode, $"The account {renamed} already exists.");
            }

            await chart!(existing.Code!, renamed, ct).ConfigureAwait(false);
        }

        var dto = (existing ?? new ElementDto()) with
        {
            DefaultVatGroupId = Cell(row, Vat) is { } group ? _vat.FirstOrDefault(v => string.Equals(VatLookups.NameOf(v), group, StringComparison.OrdinalIgnoreCase))?.Id ?? throw ValidationException.For(Vat, $"There is no VAT group {group}.") : existing?.DefaultVatGroupId,
            TypeId = type.Id!.Value, Code = renamed ?? code ?? existing?.Code, Name = name, Description = description, IdSup = parent, Values = values,
            CategoryIds = categories.Count > 0 ? categories.OfType<Guid>().ToList() : existing!.CategoryIds, Active = true,
        };
        var saved = await entity.SaveElementAsync(dto, ct).ConfigureAwait(false);
        if (units is not null && Cell(row, Units) is { } unitCodes)
        {
            var ids = unitCodes.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(u => _unitIds.TryGetValue(u, out var id) ? id : throw ValidationException.For(Units, $"There is no unit {u}.")).ToList();
            await units.SaveProductAsync(saved.Id!.Value, ids, TranslatedText.DefaultLanguage, ct).ConfigureAwait(false);
        }

        if (saved.Code is not null)
        {
            _byCode[saved.Code] = saved;
        }

        return existing is null;
    }

    private static readonly string[] Own = [Code, Name, Type, Category, Parent, Description, Units, NewCode, Vat];

    // A property may carry the name of a column of the element's own (a cost centre's trade "Name"): its column says so.
    private static string Header(TypeInfoDto property)
    {
        var name = English(property.Name);
        return Own.Contains(name, StringComparer.OrdinalIgnoreCase) ? $"{name} (property)" : name;
    }

    private static IEnumerable<TypeInfoDto> Properties(IEnumerable<ElementTypeDto> types) => types.SelectMany(t => t.Properties).Where(p => Shown.Contains(p.Kind)).OrderBy(p => p.SortOrder);

    // The table a lookup property picks from: its elements row names it, and the model holds it, so nothing from a file reaches the SQL's text.
    private async Task<string?> LookupTableAsync(TypeInfoDto property, CancellationToken ct)
    {
        var table = await db.Set<Models.Platform.Elements>().AsNoTracking().Where(e => e.Id == property.ElementsId).Select(e => e.Table).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var found = db.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == table);
        return found is null ? null : $"\"{found.GetSchema()}\".\"{found.GetTableName()}\"";
    }

    private async Task<Guid> LookupAsync(TypeInfoDto property, string code, string column, CancellationToken ct)
    {
        var table = await LookupTableAsync(property, ct).ConfigureAwait(false) ?? throw ValidationException.For(column, $"{ErrorCodes.NotConfiguredError}: the property picks from no table.");
        var nodes = (await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false)).ToArray();
        var sql = $"SELECT id AS \"Value\" FROM {table} WHERE active AND lower(code) = lower({{0}}) AND (owner_id IS NULL OR owner_id = ANY({{1}}))";
        var ids = await db.Database.SqlQueryRaw<Guid>(sql, code, nodes).ToListAsync(ct).ConfigureAwait(false);
        return ids.Count > 0 ? ids[0] : throw ValidationException.For(column, $"There is no {code}.");
    }

    // The codes of the rows the lookup values of these elements point at.
    private async Task<Dictionary<string, string>> LookupCodesAsync(IEnumerable<ElementDto> elements, CancellationToken ct)
    {
        var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = elements.ToList();
        foreach (var property in _types.SelectMany(t => t.Properties).Where(p => p.Kind == PropertyKind.Lookup))
        {
            var ids = list.Select(e => e.Values.GetValueOrDefault(property.Id!.Value)?.GetValue<string>()).OfType<string>().Select(Guid.Parse).Distinct().ToArray();
            if (ids.Length > 0 && await LookupTableAsync(property, ct).ConfigureAwait(false) is { } table)
            {
                var sql = $"SELECT id, code FROM {table} WHERE id = ANY({{0}})";
                foreach (var row in await db.Database.SqlQueryRaw<CodedRow>(sql, [ids]).ToListAsync(ct).ConfigureAwait(false))
                {
                    codes[row.Id.ToString()] = row.Code ?? string.Empty;
                }
            }
        }

        return codes;
    }

    private sealed record CodedRow(Guid Id, string? Code);

    private async Task<Dictionary<Guid, string>> UnitsOfAsync(CancellationToken ct)
    {
        if (units is null)
        {
            return [];
        }

        var codes = _unitIds.ToDictionary(u => u.Value, u => u.Key);
        var rows = await db.Set<InterProductSpecificUnit>().AsNoTracking().Where(l => l.ProductId != null && l.UnitId != null).OrderBy(l => l.SortOrder)
            .Select(l => new { l.ProductId, l.UnitId }).ToListAsync(ct).ConfigureAwait(false);
        return rows.GroupBy(r => r.ProductId!.Value).ToDictionary(g => g.Key, g => string.Join(Separator, g.Select(r => codes.GetValueOrDefault(r.UnitId!.Value))));
    }

    private static string? Text(TypeInfoDto property, JsonNode? value)
    {
        string Choice(string id) => English(property.Choices.FirstOrDefault(c => c.Id?.ToString() == id)?.Name);
        return value switch
        {
            null => null,
            JsonArray list => string.Join(Separator, list.Select(n => Choice(n!.GetValue<string>()))),
            JsonObject text => text[TranslatedText.DefaultLanguage]?.GetValue<string>() ?? text.FirstOrDefault().Value?.GetValue<string>(),
            _ when property.Kind == PropertyKind.List => Choice(value.GetValue<string>()),
            _ => value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString(),
        };
    }

    private static JsonNode Json(TypeInfoDto property, string text, string column)
    {
        string Choice(string name) => property.Choices.FirstOrDefault(c => string.Equals(English(c.Name), name, StringComparison.OrdinalIgnoreCase))?.Id?.ToString()
            ?? throw ValidationException.For(column, $"{name} is not one of its choices.");
        return property.Kind switch
        {
            PropertyKind.List => JsonValue.Create(Choice(text)),
            PropertyKind.MultiList or PropertyKind.ActivatingList => new JsonArray([.. text.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => (JsonNode?)JsonValue.Create(Choice(n)))]),
            _ => JsonValue.Create(text),
        };
    }
}

/// <summary>The categories of an entity, flat with the code of the one above.</summary>
public sealed class CategoryList(IEntityRows entity, string language) : MigrationList
{
    private const string Code = nameof(Code), Name = nameof(Name), Parent = nameof(Parent), Description = nameof(Description);

    private Dictionary<string, CategoryDto> _byCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public override async Task<int> CountAsync(CancellationToken ct) => (await entity.CategoriesAsync(ct).ConfigureAwait(false)).Count;

    public override Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct)
    {
        var columns = new List<MigrationColumn> { new(Code, true, "Rows are matched by code: an existing code is updated."), new(Name, true, "The name in English."), new(Parent, false, "The code of the category above it."), new(Description, false, "A description.") };
        if (LocalHeader(language) is { } local)
        {
            columns.Insert(2, new(local, true, $"The name in the company's language ({language})."));
        }

        return Task.FromResult<IReadOnlyList<MigrationColumn>>(columns);
    }

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        var all = await entity.CategoriesAsync(ct).ConfigureAwait(false);
        var codes = all.ToDictionary(c => c.Id!.Value, c => c.Code);
        return [.. all.OrderBy(c => c.Code, StringComparer.Ordinal).Select(c =>
        {
            var row = new Dictionary<string, object?> { [Code] = c.Code, [Name] = English(c.Name), [Parent] = c.IdSup is { } up ? codes.GetValueOrDefault(up) : null, [Description] = English(c.Description) };
            if (LocalHeader(language) is { } local)
            {
                row[local] = c.Name.GetValueOrDefault(language);
            }

            return row;
        })];
    }

    public override async Task PrepareAsync(CancellationToken ct) =>
        _byCode = new((await entity.CategoriesAsync(ct).ConfigureAwait(false)).Where(c => c.Code is not null).Select(c => KeyValuePair.Create(c.Code!, c)), StringComparer.OrdinalIgnoreCase);

    public override async Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var code = Needed(row, Code);
        if (!_seen.Add(code))
        {
            throw ValidationException.For(Code, "appears twice in the file.");
        }

        var existing = _byCode.GetValueOrDefault(code);
        Guid? parent = Cell(row, Parent) is { } above
            ? _byCode.GetValueOrDefault(above)?.Id ?? throw ValidationException.For(Parent, $"There is no category {above} above this one in the system or the file.")
            : existing?.IdSup;
        var name = Names(row, existing?.Name, language);
        var description = Cell(row, Description) is { } about ? new Dictionary<string, string> { [TranslatedText.DefaultLanguage] = about } : existing?.Description;
        _byCode[code] = await entity.SaveCategoryAsync((existing ?? new CategoryDto { IsElement = true }) with { Code = code, Name = name, Description = description, IdSup = parent, Active = true }, ct)
            .ConfigureAwait(false);
        return existing is null;
    }
}

/// <summary>Sale prices: for everyone, or for a category of clients, in a unit of the product.</summary>
public sealed class PriceList(ErpDbContext db, RequestContext request, Visibility visibility, CurrencyService currencies) : MigrationList
{
    private const string Product = nameof(Product), Unit = nameof(Unit), ClientCategory = "Client category", Currency = nameof(Currency), Price = nameof(Price);

    private Guid _company;
    private Dictionary<string, Guid> _products = new(StringComparer.OrdinalIgnoreCase), _categories = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Currency> _currencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(Guid, Guid?, Guid?, Guid)> _seen = [];

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "Prices belong to a company: choose one of its nodes.");

    public override async Task<int> CountAsync(CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        return await db.Set<ProductSalePrice>().AsNoTracking().CountAsync(p => p.CompanyId == company, ct).ConfigureAwait(false);
    }

    public override Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MigrationColumn>>(
    [
        new(Product, true, "The product's code."), new(Unit, false, "The unit's code. Empty: any unit."), new(ClientCategory, false, "The client category's code. Empty: everyone."),
        new(Currency, false, "The currency's code. Empty: the company's."), new(Price, true, "The price, a number."),
    ]);

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var rows = await (from p in db.Set<ProductSalePrice>().AsNoTracking()
                          where p.CompanyId == company
                          join product in db.Set<Product>().AsNoTracking() on p.ProductId equals product.Id
                          join link in db.Set<InterProductSpecificUnit>().AsNoTracking() on p.ProductSpecificUnitId equals link.Id into links
                          from l in links.DefaultIfEmpty()
                          join unit in db.Set<Models.Units.Unit>().AsNoTracking() on l.UnitId equals unit.Id into units
                          from u in units.DefaultIfEmpty()
                          join category in db.Set<Models.Partners.PartnerCategory>().AsNoTracking() on p.PartnerCategoryId equals category.Id into categories
                          from c in categories.DefaultIfEmpty()
                          join currency in db.Set<Currency>().AsNoTracking() on p.CurrencyId equals currency.Id into found
                          from m in found.DefaultIfEmpty()
                          orderby product.Code
                          select new { Product = product.Code, Unit = u.Code, Category = c.Code, Currency = m.Code, p.Value }).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(r => new Dictionary<string, object?> { [Product] = r.Product, [Unit] = r.Unit, [ClientCategory] = r.Category, [Currency] = r.Currency, [Price] = r.Value })];
    }

    public override async Task PrepareAsync(CancellationToken ct)
    {
        _company = await CompanyAsync(ct).ConfigureAwait(false);
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var settings = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        _products = new((await db.Set<Product>().AsNoTracking().Where(p => p.Code != null && (p.OwnerId == null || nodes.Contains(p.OwnerId.Value))).Select(p => new { p.Code, p.Id }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(p => p.Code!, StringComparer.OrdinalIgnoreCase).Select(g => KeyValuePair.Create(g.Key, g.First().Id)), StringComparer.OrdinalIgnoreCase);
        _categories = new((await db.Set<Models.Partners.PartnerCategory>().AsNoTracking().Where(c => c.Code != null && (c.OwnerId == null || settings.Contains(c.OwnerId.Value))).Select(c => new { c.Code, c.Id }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(c => c.Code!, StringComparer.OrdinalIgnoreCase).Select(g => KeyValuePair.Create(g.Key, g.First().Id)), StringComparer.OrdinalIgnoreCase);
        _currencies = new((await db.Set<Currency>().AsNoTracking().Where(c => c.Active && c.OwnerId == _company && c.Code != null).ToListAsync(ct).ConfigureAwait(false)).Select(c => KeyValuePair.Create(c.Code!, c)), StringComparer.OrdinalIgnoreCase);
    }

    public override async Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var product = _products.TryGetValue(Needed(row, Product), out var id) ? id : throw ValidationException.For(Product, $"There is no product {Cell(row, Product)}.");
        Guid? unitLink = null;
        if (Cell(row, Unit) is { } unitCode)
        {
            unitLink = await (from l in db.Set<InterProductSpecificUnit>().AsNoTracking()
                              join u in db.Set<Models.Units.Unit>().AsNoTracking() on l.UnitId equals u.Id
                              where l.ProductId == product && u.Code == unitCode
                              select (Guid?)l.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                ?? throw ValidationException.For(Unit, $"The product has no unit {unitCode} of its own: add it in the Units column of the products step.");
        }

        Guid? category = Cell(row, ClientCategory) is { } categoryCode
            ? _categories.TryGetValue(categoryCode, out var found) ? found : throw ValidationException.For(ClientCategory, $"There is no client category {categoryCode}.")
            : null;
        var currency = Cell(row, Currency) is { } currencyCode
            ? _currencies.GetValueOrDefault(currencyCode) ?? throw ValidationException.For(Currency, $"The company has no currency {currencyCode}.")
            : _currencies.Values.FirstOrDefault(c => c.IsBase) ?? throw ValidationException.For(Currency, "The company has no base currency.");
        if (!_seen.Add((product, unitLink, category, currency.Id)))
        {
            throw ValidationException.For(Product, "appears twice in the file with the same unit, category and currency.");
        }

        var value = Number(row, Price);
        if (value < 0)
        {
            throw ValidationException.For(Price, "can't be below zero.");
        }

        var rate = currency.IsBase ? 1 : await currencies.RateAsync(currency.Id, DateOnly.FromDateTime(DateTime.UtcNow), ct).ConfigureAwait(false)
            ?? throw ValidationException.For(Currency, $"{currency.Code} has no exchange rate yet: import the rates first.");
        var existing = await db.Set<ProductSalePrice>().FirstOrDefaultAsync(p => p.CompanyId == _company && p.ProductId == product && p.ProductSpecificUnitId == unitLink
            && p.PartnerCategoryId == category && p.PartnerTypeId == null && p.CurrencyId == currency.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            db.Add(new ProductSalePrice { CompanyId = _company, ProductId = product, ProductSpecificUnitId = unitLink, PartnerCategoryId = category, CurrencyId = currency.Id, Value = value,
                ValueLcu = value * rate, TenantId = request.TenantId, OwnerId = _company });
        }
        else
        {
            (existing.Value, existing.ValueLcu) = (value, value * rate);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return existing is null;
    }
}

/// <summary>Exchange rates against the company's currency, on a day.</summary>
public sealed class RateList(CurrencyService currencies) : MigrationList
{
    private const string Currency = nameof(Currency), Date = nameof(Date), Rate = nameof(Rate);

    private readonly HashSet<(string, string)> _seen = [];
    private IReadOnlyList<Gamma.Erp.Services.Currencies.CurrencyDto> _all = [];

    public override async Task<int> CountAsync(CancellationToken ct)
    {
        var count = 0;
        foreach (var currency in await currencies.CurrenciesAsync(TranslatedText.DefaultLanguage, ct).ConfigureAwait(false))
        {
            count += (await currencies.RatesAsync(currency.Id, null, null, ct).ConfigureAwait(false)).Count;
        }

        return count;
    }

    public override Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MigrationColumn>>(
        [new(Currency, true, "The currency's code."), new(Date, true, "The day, yyyy-mm-dd."), new(Rate, true, "One unit of the currency in the company's currency.")]);

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (var currency in await currencies.CurrenciesAsync(TranslatedText.DefaultLanguage, ct).ConfigureAwait(false))
        {
            rows.AddRange((await currencies.RatesAsync(currency.Id, null, null, ct).ConfigureAwait(false)).Select(r =>
                new Dictionary<string, object?> { [Currency] = currency.Code, [Date] = r.RateDate, [Rate] = r.Rate }));
        }

        return rows;
    }

    public override async Task PrepareAsync(CancellationToken ct) => _all = await currencies.CurrenciesAsync(TranslatedText.DefaultLanguage, ct).ConfigureAwait(false);

    public override async Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var code = Needed(row, Currency);
        var currency = _all.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)) ?? throw ValidationException.For(Currency, $"The company has no currency {code}.");
        if (!DateOnly.TryParseExact(Needed(row, Date), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw ValidationException.For(Date, "is not a date, yyyy-mm-dd.");
        }

        if (!_seen.Add((code.ToUpperInvariant(), day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))))
        {
            throw ValidationException.For(Date, "appears twice in the file for this currency.");
        }

        var had = (await currencies.RatesAsync(currency.Id, day, day, ct).ConfigureAwait(false)).Count > 0;
        await currencies.SetRateAsync(new SetRateRequest(currency.Id, day, Number(row, Rate)), ct).ConfigureAwait(false);
        return !had;
    }
}

/// <summary>Where each document type's numbering stands: the next document takes this number, so the numbering carries on.</summary>
public sealed class NumberList(ErpDbContext db, Visibility visibility) : MigrationList
{
    private const string Code = nameof(Code), Name = nameof(Name), Parent = nameof(Parent), Next = "Next number";

    private List<DocumentType> _types = [];

    private async Task<List<DocumentType>> TypesAsync(CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        return await db.Set<DocumentType>().AsNoTracking().Where(t => t.OwnerId != null && nodes.Contains(t.OwnerId.Value)).OrderBy(t => t.Code).ToListAsync(ct).ConfigureAwait(false);
    }

    public override async Task<int> CountAsync(CancellationToken ct) => (await TypesAsync(ct).ConfigureAwait(false)).Count;

    public override Task<IReadOnlyList<MigrationColumn>> ColumnsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MigrationColumn>>(
    [
        new(Code, true, "The document type's code."), new(Name, false, "The type's name, for reading only."),
        new(Parent, false, "The code of the type it sits under, when two types share a code. Empty: the one at the top."), new(Next, true, "The number the next document of the type takes."),
    ]);

    public override async Task<IReadOnlyList<Dictionary<string, object?>>> RowsAsync(CancellationToken ct)
    {
        var types = await TypesAsync(ct).ConfigureAwait(false);
        var codes = types.ToDictionary(t => t.Id, t => t.Code);
        return [.. types.Select(t => new Dictionary<string, object?>
        {
            [Code] = t.Code, [Name] = t.Name.In(TranslatedText.DefaultLanguage), [Parent] = t.IdSup is { } up ? codes.GetValueOrDefault(up) : null, [Next] = t.CodeCurrent ?? t.CodeStart,
        })];
    }

    public override async Task PrepareAsync(CancellationToken ct) => _types = await TypesAsync(ct).ConfigureAwait(false);

    public override async Task<bool> SaveAsync(IReadOnlyDictionary<string, string> row, CancellationToken ct)
    {
        var code = Needed(row, Code);
        var same = _types.Where(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase)).ToList();
        if (same.Count > 1)
        {
            var parent = Cell(row, Parent);
            var parents = _types.ToDictionary(t => t.Id, t => t.Code);
            same = [.. same.Where(t => parent is null ? t.IdSup is null : t.IdSup is { } up && string.Equals(parents.GetValueOrDefault(up), parent, StringComparison.OrdinalIgnoreCase))];
        }

        var type = same.Count switch
        {
            0 => throw ValidationException.For(Code, $"There is no document type {code}{(Cell(row, Parent) is { } under ? $" under {under}" : string.Empty)}."),
            1 => same[0],
            _ => throw ValidationException.For(Parent, "Several types share this code here: the configuration needs a look."),
        };
        var next = Number(row, Next);
        if (next != decimal.Truncate(next) || next < (type.CodeStart ?? 1) || (type.CodeEnd is { } end && next > end))
        {
            throw ValidationException.For(Next, $"must be a whole number from {type.CodeStart ?? 1}{(type.CodeEnd is { } last ? $" to {last}" : string.Empty)}.");
        }

        var stored = await db.Set<DocumentType>().FirstAsync(t => t.Id == type.Id, ct).ConfigureAwait(false);
        stored.CodeCurrent = (int)next;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return false;
    }
}

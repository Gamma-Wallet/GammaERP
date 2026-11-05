using System.Reflection;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Documents;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Configuration;

/// <summary>
/// A column of a configuration model as a screen edits it (B6, C3 phase 5): its name as the JSON has it, what it holds, whether it must be given,
/// and, for a link, the model it points at (null when it points at a row outside the configuration, such as a product or an account).
/// Kinds: text, translated (a text per language), number, integer, boolean, date, time, datetime, id (a link), json.
/// </summary>
public sealed record ConfigColumnDto(string Name, string Kind, bool Required, bool IsLink, string? LinkModel);

public sealed record ConfigSchemaDto(string Model, IReadOnlyList<ConfigColumnDto> Columns);

/// <summary>A row a link column may point at: its id, code and name in the language asked for.</summary>
public sealed record ConfigChoiceDto(Guid Id, string? Code, string Name);

/// <summary>Who may use a document type: users and roles.</summary>
public sealed record DocumentTypeUsersDto(IReadOnlyList<Guid> UserIds, IReadOnlyList<Guid> RoleIds);

public sealed partial class ConfigurationService
{
    /// <summary>The models whose rows are not a tree: their code never reads "Under" (IdSup), so the form does not offer it.</summary>
    private static readonly HashSet<Type> NotATree = [typeof(Models.Opportunities.OpportunityStage), typeof(Models.Products.SaleMenu),
        typeof(Models.Products.ProductSalePrice), typeof(Models.Units.UnitConversion)];

    /// <summary>The model's editable columns, read from the database model itself (types, nullability, foreign keys): no rule of its own.</summary>
    public ConfigSchemaDto Schema(string model)
    {
        var type = TypeOf(model);
        var entity = db.Model.FindEntityType(type)!;
        var links = entity.GetForeignKeys().Where(k => k.Properties.Count == 1).ToDictionary(k => k.Properties[0].Name, k => k.PrincipalEntityType.ClrType);
        var nullability = new NullabilityInfoContext();
        var columns = Columns(type).Where(p => !Kept.Contains(p.Name) && !(p.Name == nameof(Row.IdSup) && NotATree.Contains(type))).Select(p =>
        {
            var property = entity.FindProperty(p.Name);
            var kind = KindOf(p.PropertyType);
            var required = p.PropertyType == typeof(bool) ? false
                : property is not null ? !property.IsNullable
                : nullability.Create(p).WriteState == NullabilityState.NotNull;
            var target = links.GetValueOrDefault(p.Name);
            var linkModel = target is null ? null : Models.FirstOrDefault(m => m.Value == target).Key;
            return new ConfigColumnDto(Camel(p.Name), kind, required, target is not null, linkModel);
        }).ToList();
        return new ConfigSchemaDto(model, columns);
    }

    /// <summary>
    /// The rows a link column may point at, those the company sees (its own and shared ones), matching a code or name; at most 200. A row with
    /// no code or name is named by the rows it links to, an entity scope by its name, category and type: never by its id.
    /// </summary>
    public async Task<IReadOnlyList<ConfigChoiceDto>> ChoicesAsync(string model, string column, string? search, string language, CancellationToken ct)
    {
        var type = TypeOf(model);
        var key = db.Model.FindEntityType(type)!.GetForeignKeys().FirstOrDefault(k => k.Properties.Count == 1
            && string.Equals(k.Properties[0].Name, column, StringComparison.OrdinalIgnoreCase))
            ?? throw ValidationException.For(column, $"{model}.{column} is not a link.");
        var principal = key.PrincipalEntityType.ClrType;
        if (!typeof(Row).IsAssignableFrom(principal))
        {
            return [];
        }

        var nodes = (await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false)).Concat(await visibility.NodesAsync(ct).ConfigureAwait(false)).ToHashSet();
        var rows = (await Query(principal).ToListAsync(ct).ConfigureAwait(false)).Where(r => r.Active && (r.OwnerId is null || nodes.Contains(r.OwnerId.Value))).ToList();
        var code = principal.GetProperty("Code");
        var labels = await LabelsAsync(principal, rows, language, ct).ConfigureAwait(false);
        var term = search?.Trim();
        return [.. rows.Select(r => new ConfigChoiceDto(r.Id, code?.GetValue(r) as string, labels.GetValueOrDefault(r.Id) ?? "—"))
            .Where(c => string.IsNullOrEmpty(term) || c.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || (c.Code?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(c => c.Code ?? c.Name, StringComparer.CurrentCulture).Take(200)];
    }

    /// <summary>What a row is called: its name or code; else its entity scope's parts; else the names of the rows it links to.</summary>
    private async Task<Dictionary<Guid, string?>> LabelsAsync(Type type, List<Row> rows, string language, CancellationToken ct)
    {
        if (type == typeof(Models.Platform.Elements))
        {
            var parts = new Dictionary<Guid, string?>();
            foreach (var table in rows.Cast<Models.Platform.Elements>().Select(e => e.Table).OfType<string>().Distinct())
            {
                foreach (var part in db.Model.GetEntityTypes().Where(t => t.GetTableName() == $"{table}_category" || t.GetTableName() == $"{table}_type"))
                {
                    foreach (var (id, name) in await NamesAsync(part.ClrType, language, ct).ConfigureAwait(false))
                    {
                        parts[id] = name;
                    }
                }
            }

            return rows.Cast<Models.Platform.Elements>().ToDictionary(e => e.Id, e =>
            {
                var shown = new[] { e.Name?.In(language), e.CategoryId is { } c ? parts.GetValueOrDefault(c) : null, e.TypeId is { } t ? parts.GetValueOrDefault(t) : null }
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
                return shown.Count == 0 ? null : string.Join(" · ", shown);
            });
        }

        var own = rows.ToDictionary(r => r.Id, r => NameOf(r, language));
        if (own.Values.All(n => n is not null))
        {
            return own;
        }

        // No name of its own (a line scope): the rows it links to name it, in the order of its columns; the row's own tree and what every row has are left out.
        var links = db.Model.FindEntityType(type)!.GetForeignKeys().Where(k => k.Properties.Count == 1 && k.PrincipalEntityType.ClrType != type && typeof(Row).IsAssignableFrom(k.PrincipalEntityType.ClrType)
            && typeof(Row).GetProperty(k.Properties[0].Name) is null).OrderBy(k => Array.FindIndex(type.GetProperties(), p => p.Name == k.Properties[0].Name)).Take(2).ToList();
        var names = new List<(System.Reflection.PropertyInfo Column, Dictionary<Guid, string?> Names)>();
        foreach (var link in links)
        {
            if (type.GetProperty(link.Properties[0].Name) is { } column)
            {
                var parents = await Query(link.PrincipalEntityType.ClrType).ToListAsync(ct).ConfigureAwait(false);
                names.Add((column, await LabelsAsync(link.PrincipalEntityType.ClrType, parents, language, ct).ConfigureAwait(false)));
            }
        }

        return rows.ToDictionary(r => r.Id, r =>
        {
            if (own[r.Id] is { } name)
            {
                return name;
            }

            var shown = names.Select(n => n.Column.GetValue(r) is Guid id ? n.Names.GetValueOrDefault(id) : null).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
            return shown.Count == 0 ? null : string.Join(" · ", shown);
        });
    }

    private async Task<Dictionary<Guid, string?>> NamesAsync(Type type, string language, CancellationToken ct) =>
        (await Query(type).ToListAsync(ct).ConfigureAwait(false)).ToDictionary(r => r.Id, r => NameOf(r, language));

    private static string? NameOf(Row row, string language)
    {
        var type = row.GetType();
        return (type.GetProperty("Name")?.GetValue(row) switch
        {
            TranslatedText text => text.In(language),
            string plain => plain,
            _ => null,
        }) ?? type.GetProperty("Code")?.GetValue(row) as string;
    }

    /// <summary>A document type's own settings (B7): whether it posts, partial reversal, whether it is a reversal and of which type.</summary>
    public async Task<DocumentTypeSettings?> DocumentTypeAsync(Guid id, CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var type = await db.Set<DocumentType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.Active, ct).ConfigureAwait(false);
        return type is null || (type.OwnerId is { } owner && !nodes.Contains(owner)) ? null
            : new DocumentTypeSettings(type.NeverPosted, type.AllowsPartialReversal, type.IsReversal, type.ReversedDocumentTypeId, type.SendsToProduction, type.ProductionDocumentTypeId, type.SellsProducedProduct, type.StartsRepair, type.CanBeAccepted,
                type.HoldsOpenItem, type.PaysOpenItems, type.BillsByPlan, type.NeedsApproval, type.SellsAssets, type.ProducedOnProductionScreen);
    }

    /// <summary>The users and roles a document type is given to (B7).</summary>
    public async Task<DocumentTypeUsersDto> DocumentTypeUsersAsync(Guid id, CancellationToken ct)
    {
        var links = await db.Set<InterDocumentTypeUser>().AsNoTracking().Where(l => l.DocumentTypeId == id).ToListAsync(ct).ConfigureAwait(false);
        return new DocumentTypeUsersDto([.. links.Where(l => l.UserId is not null).Select(l => l.UserId!.Value)],
            [.. links.Where(l => l.RoleId is not null).Select(l => l.RoleId!.Value)]);
    }

    private static string KindOf(Type type)
    {
        var plain = Nullable.GetUnderlyingType(type) ?? type;
        return plain == typeof(string) ? "text"
            : plain == typeof(TranslatedText) ? "translated"
            : plain == typeof(bool) ? "boolean"
            : plain == typeof(int) || plain == typeof(long) || plain == typeof(short) ? "integer"
            : plain == typeof(decimal) || plain == typeof(double) || plain == typeof(float) ? "number"
            : plain == typeof(DateOnly) ? "date"
            : plain == typeof(TimeOnly) ? "time"
            : plain == typeof(DateTimeOffset) || plain == typeof(DateTime) ? "datetime"
            : plain == typeof(Guid) ? "id"
            : "json";
    }
}

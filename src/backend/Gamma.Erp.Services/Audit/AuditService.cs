using System.Globalization;
using System.Text.Json;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gamma.Erp.Services.Audit;

/// <summary>A user the activity can be filtered by.</summary>
public sealed record AuditUserDto(Guid Id, string? Name);

/// <summary>One sign-in, sign-out or wrong password.</summary>
public sealed record SignInRowDto(Guid Id, DateTimeOffset At, Guid UserId, string? User, string Event);

/// <summary>What was done to one table in the period.</summary>
public sealed record ActivityTableDto(string Table, string Label, int Added, int Changed, int Deleted);

/// <summary>One version of a row in the period: a row of a document says which one.</summary>
public sealed record ActivityRowDto(Guid Id, DateTimeOffset At, string User, string Action, string? Record, Guid? DocumentId, string? Document);

/// <summary>A field of a row: for a change the value before and after, for an addition after only, for a deletion before only.</summary>
public sealed record ActivityFieldDto(string Field, string? Before, string? After);

/// <summary>
/// Who did what. Nothing is written for it: every version of every row already says who wrote it and when, so the activity
/// is read from there, from the first day. The first version of a row is its addition, a switched-off row that is no history copy its deletion,
/// any other version a change. Whoever has the screen in their menu may read it (Menu access); the tables come from the model, never from a request.
/// </summary>
public sealed class AuditService(ErpDbContext db, Visibility visibility)
{
    /// <summary>The menu screen that opens this: Menu access on its row decides who reads it.</summary>
    public const string Screen = "user-activity";

    /// <summary>The rows one table shows at most.</summary>
    public const int MaxRows = 500;

    private const string Added = nameof(Added), Changed = nameof(Changed), Deleted = nameof(Deleted);

    /// <summary>Who a row with no user was written by (sign-up, set-up).</summary>
    private const string NoUser = "System";

    private static readonly string[] LabelColumns = ["username", "code", "name"];

    public async Task<IReadOnlyList<AuditUserDto>> UsersAsync(CancellationToken ct)
    {
        await AllowedAsync(ct).ConfigureAwait(false);
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var users = await db.Set<User>().AsNoTracking().Where(u => u.OwnerId == null || nodes.Contains(u.OwnerId.Value)).OrderBy(u => u.Username).ToListAsync(ct).ConfigureAwait(false);
        return [.. users.Select(u => new AuditUserDto(u.Id, u.Username))];
    }

    public async Task<IReadOnlyList<SignInRowDto>> SignInsAsync(DateOnly from, DateOnly to, Guid? userId, CancellationToken ct)
    {
        await AllowedAsync(ct).ConfigureAwait(false);
        var (start, end) = Period(from, to);
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var rows = await db.Set<Login>().AsNoTracking()
            .Where(l => l.CreatedOn >= start && l.CreatedOn < end && (userId == null || l.UserId == userId) && (l.OwnerId == null || nodes.Contains(l.OwnerId.Value)))
            .OrderByDescending(l => l.CreatedOn).Take(MaxRows).ToListAsync(ct).ConfigureAwait(false);
        var ids = rows.Select(r => r.UserId).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        return [.. rows.Select(r => new SignInRowDto(r.Id, r.CreatedOn, r.UserId, users.GetValueOrDefault(r.UserId), r.Failed ? "Wrong password" : r.InOut ? "Signed in" : "Signed out"))];
    }

    public async Task<IReadOnlyList<ActivityTableDto>> ActivityAsync(DateOnly from, DateOnly to, Guid? userId, CancellationToken ct)
    {
        await AllowedAsync(ct).ConfigureAwait(false);
        var (where, parameters) = await FilterAsync(from, to, userId, ct).ConfigureAwait(false);
        var union = string.Join(" UNION ALL ", Tables().Select(t => $"SELECT '{t.ClrType.Name}' AS table_key, {ActionOf(t)} AS action FROM {Quoted(t)} r WHERE {where}"));
        var sql = $"SELECT table_key, action, count(*)::int AS rows FROM ({union}) a GROUP BY 1, 2";
        var counts = await db.Database.SqlQueryRaw<ActionCount>(sql, parameters)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. counts.GroupBy(c => c.TableKey).Select(g => new ActivityTableDto(g.Key, Words(g.Key), Count(g, Added), Count(g, Changed), Count(g, Deleted)))
            .OrderBy(t => t.Label, StringComparer.Ordinal)];

        static int Count(IEnumerable<ActionCount> rows, string action) => rows.Where(r => r.Action == action).Sum(r => r.Rows);
    }

    public async Task<IReadOnlyList<ActivityRowDto>> RowsAsync(string table, DateOnly from, DateOnly to, Guid? userId, string language, CancellationToken ct)
    {
        await AllowedAsync(ct).ConfigureAwait(false);
        var entity = Table(table);
        var (where, parameters) = await FilterAsync(from, to, userId, ct).ConfigureAwait(false);
        var sql = $"SELECT r.id, r.created_on, r.created_by, {ActionOf(entity)} AS action, to_jsonb(r)::text AS data FROM {Quoted(entity)} r WHERE {where} ORDER BY r.created_on DESC LIMIT {MaxRows}";
        var rows = await db.Database.SqlQueryRaw<VersionRow>(sql, parameters).ToListAsync(ct).ConfigureAwait(false);

        var writers = rows.Select(r => r.CreatedBy).OfType<Guid>().Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => writers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        var documents = await DocumentsOfAsync(entity, rows, ct).ConfigureAwait(false);
        var ids = documents.Values.Distinct().ToList();
        var codes = await db.Set<Document>().AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Code, ct).ConfigureAwait(false);
        return [.. rows.Select(r =>
        {
            Guid? document = documents.TryGetValue(r.Id, out var id) ? id : null;
            return new ActivityRowDto(r.Id, r.CreatedOn, r.CreatedBy is { } by ? users.GetValueOrDefault(by) ?? NoUser : NoUser, r.Action, Label(Parse(r.Data), language),
                document, document is { } d ? codes.GetValueOrDefault(d) : null);
        })];
    }

    /// <summary>What one version of a row did, field by field, with the names of the rows it points at: never their ids, and never a password.</summary>
    public async Task<IReadOnlyList<ActivityFieldDto>> ChangeAsync(string table, Guid id, string language, CancellationToken ct)
    {
        await AllowedAsync(ct).ConfigureAwait(false);
        var entity = Table(table);
        var version = await VersionAsync(entity, id, ct).ConfigureAwait(false);
        if (version is not { } found || !await InScopeAsync(found.Row, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("row", "There is no such row.");
        }

        var (row, action) = found;
        JsonElement? before = GuidOf(row.GetProperty("id_change")) is { } older ? (await VersionAsync(entity, older, ct).ConfigureAwait(false))?.Row : null;
        var fields = entity.GetProperties().Where(Shown).Select(p =>
        {
            var name = p.GetColumnName();
            var old = before is { } b && b.TryGetProperty(name, out var o) ? o : default;
            var now = row.TryGetProperty(name, out var n) ? n : default;
            return (Property: p, Old: action == Deleted ? now : old, Now: action == Deleted ? default : now);
        }).Where(f => action == Changed ? Raw(f.Old) != Raw(f.Now) : !Empty(f.Old) || !Empty(f.Now)).ToList();

        var ids = fields.SelectMany(f => new[] { f.Old, f.Now }).Select(GuidOf).OfType<Guid>().Distinct().ToArray();
        var names = await NamesAsync(ids, language, ct).ConfigureAwait(false);
        return [.. fields.Select(f => new ActivityFieldDto(Field(f.Property.Name), Text(f.Old, names, language), Text(f.Now, names, language)))];
    }

    private async Task AllowedAsync(CancellationToken ct)
    {
        if (!await visibility.OpensScreenAsync(Screen, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: User activity is not in your menu.");
        }
    }

    // The tables with the standard columns; sign-ins are a list of their own.
    private IEnumerable<IEntityType> Tables() => db.Model.GetEntityTypes()
        .Where(e => e.BaseType is null && !e.ClrType.IsAbstract && typeof(Row).IsAssignableFrom(e.ClrType) && e.ClrType != typeof(Login) && e.GetTableName() is not null)
        .GroupBy(e => (e.GetSchema(), e.GetTableName())).Select(g => g.First());

    private IEntityType Table(string name) => Tables().FirstOrDefault(t => t.ClrType.Name == name)
        ?? throw ValidationException.For("table", "There is no such table.");

    private static string Quoted(IEntityType entity) => $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";

    private static string ActionOf(IEntityType entity) =>
        $"CASE WHEN r.id_change IS NULL THEN '{Added}' WHEN NOT r.active AND NOT EXISTS (SELECT 1 FROM {Quoted(entity)} n WHERE n.id_change = r.id) THEN '{Deleted}' ELSE '{Changed}' END";

    // Dates are whole days, both included.
    private static (DateTimeOffset Start, DateTimeOffset End) Period(DateOnly from, DateOnly to) =>
        (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    // What every table is read by: the period, the user, and the request's nodes (a row of no node is the organisation's own).
    private async Task<(string Where, object[] Parameters)> FilterAsync(DateOnly from, DateOnly to, Guid? userId, CancellationToken ct)
    {
        var (start, end) = Period(from, to);
        var nodes = (await visibility.NodesAsync(ct).ConfigureAwait(false)).ToArray();
        const string where = "r.created_on >= {0} AND r.created_on < {1} AND (r.owner_id IS NULL OR r.owner_id = ANY({2}))";
        return userId is { } user ? (where + " AND r.created_by = {3}", [start, end, nodes, user]) : (where, [start, end, nodes]);
    }

    private async Task<(JsonElement Row, string Action)?> VersionAsync(IEntityType entity, Guid id, CancellationToken ct)
    {
        var sql = $"SELECT r.id, r.created_on, r.created_by, {ActionOf(entity)} AS action, to_jsonb(r)::text AS data FROM {Quoted(entity)} r WHERE r.id = {{0}}";
        var rows = await db.Database.SqlQueryRaw<VersionRow>(sql, id).ToListAsync(ct).ConfigureAwait(false);
        return rows.Count == 0 ? null : (Parse(rows[0].Data), rows[0].Action);
    }

    private async Task<bool> InScopeAsync(JsonElement row, CancellationToken ct) =>
        GuidOf(row.GetProperty("owner_id")) is not { } owner || (await visibility.NodesAsync(ct).ConfigureAwait(false)).Contains(owner);

    // A row of a document, by the document it points at; a document's own older version, by the document it became.
    private async Task<Dictionary<Guid, Guid>> DocumentsOfAsync(IEntityType entity, List<VersionRow> rows, CancellationToken ct)
    {
        if (entity.ClrType == typeof(Document))
        {
            var live = rows.ToDictionary(r => r.Id, r => r.Id);
            var open = rows.Select(r => r.Id).ToArray();
            while (open.Length > 0)
            {
                var sql = $"SELECT id, id_change FROM {Quoted(entity)} WHERE id_change = ANY({{0}})";
                var newer = await db.Database.SqlQueryRaw<NewerVersion>(sql, [open]).ToListAsync(ct).ConfigureAwait(false);
                var moved = newer.ToDictionary(n => n.IdChange, n => n.Id);
                foreach (var key in live.Keys.ToList())
                {
                    live[key] = moved.GetValueOrDefault(live[key], live[key]);
                }

                open = [.. newer.Select(n => n.Id)];
            }

            return live;
        }

        var column = entity.GetForeignKeys().FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(Document) && f.Properties.Count == 1)?.Properties[0].GetColumnName();
        return column is null ? [] : rows.Select(r => (r.Id, Document: GuidOf(Parse(r.Data).GetProperty(column)))).Where(x => x.Document is not null)
            .ToDictionary(x => x.Id, x => x.Document!.Value);
    }

    // An id may point at any table, so each is looked up by its key; deleted rows and older versions keep their names.
    private async Task<Dictionary<Guid, string?>> NamesAsync(Guid[] ids, string language, CancellationToken ct)
    {
        if (ids.Length == 0)
        {
            return [];
        }

        var union = string.Join(" UNION ALL ", Tables().Select(t => $"SELECT r.id, to_jsonb(r)::text AS data FROM {Quoted(t)} r WHERE r.id = ANY({{0}})"));
        var rows = await db.Database.SqlQueryRaw<NamedRow>(union, [ids]).ToListAsync(ct).ConfigureAwait(false);
        return rows.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => Label(Parse(g.First().Data), language));
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    // What a row is called: its user name, or the code and name it has.
    private static string? Label(JsonElement row, string language)
    {
        var parts = LabelColumns.Select(p => row.TryGetProperty(p, out var v) ? Plain(v, language) : null).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private static string? Plain(JsonElement value, string language) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Object => Translated(value, language) ?? value.GetRawText(),
        JsonValueKind.String => Translated(value.GetString()!, language),
        _ => value.GetRawText(),
    };

    // A translated name is kept as JSON text, so to_jsonb gives it back as a string.
    private static string Translated(string value, string language)
    {
        if (!value.StartsWith('{'))
        {
            return value;
        }

        try
        {
            return Translated(Parse(value), language) ?? value;
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static string? Translated(JsonElement value, string language) =>
        value.TryGetProperty(language, out var own) && own.ValueKind == JsonValueKind.String ? own.GetString()
        : value.TryGetProperty(TranslatedText.DefaultLanguage, out var english) && english.ValueKind == JsonValueKind.String ? english.GetString() : null;

    private static string? Text(JsonElement value, Dictionary<Guid, string?> names, string language) =>
        GuidOf(value) is { } id ? names.GetValueOrDefault(id) ?? "(no name)" : Plain(value, language);

    private static string Raw(JsonElement value) => value.ValueKind == JsonValueKind.Undefined ? "null" : value.GetRawText();

    private static bool Empty(JsonElement value) => value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;

    private static Guid? GuidOf(JsonElement value) => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id) ? id : null;

    // Not the row's own bookkeeping, and never a password's or a code's hash or the lockout counters.
    private static bool Shown(IProperty property) =>
        property.Name is not (nameof(Row.Id) or nameof(Row.TenantId) or nameof(Row.OwnerId) or nameof(Row.IdChange) or nameof(Row.Active) or nameof(Row.CreatedOn) or nameof(Row.CreatedBy)
            or nameof(User.PasswordHash) or nameof(PasswordResetCode.CodeHash) or nameof(User.AccessFailedCount) or nameof(User.LockoutEnd));

    private static string Field(string property) =>
        Words(property == nameof(Row.IdSup) ? "Parent" : property.Length > 2 && property.EndsWith("Id", StringComparison.Ordinal) ? property[..^2] : property);

    // "PartnerTypeInfo" reads "Partner type info".
    private static string Words(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLower(c, CultureInfo.InvariantCulture) : c.ToString()));

    private sealed record ActionCount(string TableKey, string Action, int Rows);

    private sealed record VersionRow(Guid Id, DateTimeOffset CreatedOn, Guid? CreatedBy, string Action, string Data);

    private sealed record NewerVersion(Guid Id, Guid IdChange);

    private sealed record NamedRow(Guid Id, string Data);
}

using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Api.Auth;

/// <summary>A right on one category of an entity: read all, read own, write, forward.</summary>
public sealed record RightDto(string Table, Guid CategoryId, bool ReadAll, bool ReadOwn, bool Write, bool Forward);

public sealed record PasswordChange(string Current, string New);

public sealed record PasswordReset(string New);

public sealed record UsernameChange(string Username);

public sealed record UserEmail(string? Email);

/// <summary>A user's sign-in details, for the Sign-in tab.</summary>
public sealed record UserSignIn(string? Username, string? Email);

public sealed record LanguageChoice(string? Language);

/// <summary>The signed-in user's own rights, so the screens show only what they may do: an administrator may do everything.</summary>
public sealed record MyRightsDto(bool IsAdministrator, IReadOnlyList<RightDto> Rights);

/// <summary>
/// Who may do what: the rights of a user or a role per entity and
/// category, the cost centres a user works at (counters, halls, tables, shops), a user's own password and an administrator's reset. Only an
/// administrator changes rights and cost centres; a password is kept only as its hash.
/// </summary>
public sealed class AccessService(ErpDbContext db, RequestContext request, IServiceScopeFactory scopes)
{
    private static readonly PasswordHasher<User> Hasher = new();

    public async Task<IReadOnlyList<RightDto>> RightsAsync(Guid? userId, Guid? roleId, CancellationToken ct)
    {
        var entities = await EntitiesAsync(ct).ConfigureAwait(false);
        var rows = await db.Set<ElementsAccess>().AsNoTracking()
            .Where(a => a.ElementsId != null && entities.Keys.Contains(a.ElementsId.Value) && a.CategoryId != null
                && ((userId != null && a.UserId == userId) || (roleId != null && a.RoleId == roleId)))
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(a => new RightDto(entities[a.ElementsId!.Value], a.CategoryId!.Value, a.ReadAll, a.ReadOwn, a.Write, a.Forward))
            .OrderBy(r => r.Table, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The signed-in user's rights per entity and category: their own and their roles', added together as the lists apply them
    /// (<see cref="Visibility.RightsAsync"/>). An administrator gets none listed, and IsAdministrator.
    /// </summary>
    public async Task<MyRightsDto> MyRightsAsync(CancellationToken ct)
    {
        if (request.IsAdministrator)
        {
            return new MyRightsDto(true, []);
        }

        var entities = await EntitiesAsync(ct).ConfigureAwait(false);
        var userId = request.UserId;
        var roles = request.RoleIds.ToList();
        var rows = await db.Set<ElementsAccess>().AsNoTracking()
            .Where(a => a.ElementsId != null && entities.Keys.Contains(a.ElementsId.Value) && a.CategoryId != null
                && ((a.UserId != null && a.UserId == userId) || (a.RoleId != null && roles.Contains(a.RoleId.Value))))
            .ToListAsync(ct).ConfigureAwait(false);
        return new MyRightsDto(false, [.. rows.GroupBy(a => (Table: entities[a.ElementsId!.Value], Category: a.CategoryId!.Value))
            .Select(g => new RightDto(g.Key.Table, g.Key.Category, g.Any(a => a.ReadAll), g.Any(a => a.ReadOwn), g.Any(a => a.Write), g.Any(a => a.Forward)))
            .OrderBy(r => r.Table, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// An administrator gives a user the name they sign in with (a user made in the users register has none until then): 3 to 64 letters,
    /// digits, dots, dashes or underscores, kept in lower case, unique across the whole system.
    /// </summary>
    public async Task SetUsernameAsync(Guid userId, UsernameChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        Administrator();
        var name = SignInService.Normalize(change.Username);
        if (name.Length is < 3 or > 64 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            throw ValidationException.For("username", "A user name has 3 to 64 letters, digits, dots, dashes or underscores.");
        }

        var user = await db.Set<User>().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("userId", "There is no such user.");
        if (user.Username == name)
        {
            return;
        }

        // Sign-in comes before the corporate is known, so the name is unique across every corporate, not just this one.
        if (await UsernameTakenAsync(name, userId, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("username", $"{ErrorCodes.NameError}: the user name {name} is taken.");
        }

        user.Username = name;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether another user anywhere has the name. Row security shows this request only its own corporate's users, so the check runs in a scope with no tenant,
    /// as sign-in does; otherwise a name taken in another corporate passes here and the unique index refuses it with a raw error.
    /// </summary>
    private async Task<bool> UsernameTakenAsync(string name, Guid userId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var everyone = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        return await everyone.Set<User>().IgnoreQueryFilters().AsNoTracking().AnyAsync(u => u.Username == name && u.Id != userId, ct).ConfigureAwait(false);
    }

    /// <summary>The rights of a user or a role become exactly these (others are switched off), each on an entity and one of its categories.</summary>
    public async Task<IReadOnlyList<RightDto>> SetRightsAsync(Guid? userId, Guid? roleId, IReadOnlyList<RightDto> rights, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rights);
        Administrator();
        if (userId is { } u && !await db.Set<User>().AnyAsync(x => x.Id == u, ct).ConfigureAwait(false)
            || roleId is { } r && !await db.Set<UserCategory>().AnyAsync(x => x.Id == r, ct).ConfigureAwait(false))
        {
            throw ValidationException.For(userId is null ? "roleId" : "userId", "There is no such user or role.");
        }

        var entities = await EntitiesAsync(ct).ConfigureAwait(false);
        var byTable = entities.GroupBy(e => e.Value, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
        var errors = new Dictionary<string, string>();
        foreach (var (right, i) in rights.Select((x, i) => (x, i)))
        {
            if (!byTable.ContainsKey(right.Table))
            {
                errors[$"rights[{i}].table"] = $"There is no entity {right.Table}.";
            }
            else if (!await CategoryExistsAsync(right.Table, right.CategoryId, ct).ConfigureAwait(false))
            {
                errors[$"rights[{i}].categoryId"] = $"{right.Table} has no such category.";
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var existing = await db.Set<ElementsAccess>().Where(a => a.ElementsId != null && entities.Keys.Contains(a.ElementsId.Value)
            && ((userId != null && a.UserId == userId) || (roleId != null && a.RoleId == roleId))).ToListAsync(ct).ConfigureAwait(false);
        foreach (var right in rights)
        {
            var row = existing.FirstOrDefault(a => a.ElementsId == byTable[right.Table] && a.CategoryId == right.CategoryId);
            if (row is null)
            {
                row = new ElementsAccess { UserId = userId, RoleId = roleId, ElementsId = byTable[right.Table], CategoryId = right.CategoryId, TenantId = request.TenantId };
                db.Add(row);
                existing.Add(row);
            }

            (row.ReadAll, row.ReadOwn, row.Write, row.Forward) = (right.ReadAll, right.ReadOwn, right.Write, right.Forward);
        }

        foreach (var gone in existing.Where(a => !rights.Any(r => byTable[r.Table] == a.ElementsId && r.CategoryId == a.CategoryId)))
        {
            gone.Active = false;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await RightsAsync(userId, roleId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> CostCentersAsync(Guid userId, CancellationToken ct) =>
        await db.Set<InterUserCostCenter>().AsNoTracking().Where(l => l.UserId == userId && l.CostCenterId != null).Select(l => l.CostCenterId!.Value)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <summary>The cost centres a user works at become exactly these (the user signs in at one of them).</summary>
    public async Task<IReadOnlyList<Guid>> SetCostCentersAsync(Guid userId, IReadOnlyList<Guid> costCenters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(costCenters);
        Administrator();
        if (!await db.Set<User>().AnyAsync(u => u.Id == userId, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("userId", "There is no such user.");
        }

        // Any cost centre of the tree below the request's node, the companies under a group included (where people work is structure, not a
        // company's data): an administrator at the group links people to its companies.
        var known = new HashSet<Guid>();
        foreach (var place in costCenters.Distinct())
        {
            if (await Workplaces.MayWorkAtAsync(db, [request.NodeId], place, ct).ConfigureAwait(false)
                && await db.Set<CostCenter>().AsNoTracking().AnyAsync(c => c.Id == place, ct).ConfigureAwait(false))
            {
                known.Add(place);
            }
        }

        if (costCenters.FirstOrDefault(c => !known.Contains(c)) is { } missing && missing != Guid.Empty)
        {
            throw ValidationException.For("costCenters", $"There is no cost centre {missing} here.");
        }

        var links = await db.Set<InterUserCostCenter>().Where(l => l.UserId == userId).ToListAsync(ct).ConfigureAwait(false);
        foreach (var gone in links.Where(l => l.CostCenterId is not { } c || !costCenters.Contains(c)))
        {
            gone.Active = false;
        }

        foreach (var added in costCenters.Distinct().Where(c => links.All(l => l.CostCenterId != c)))
        {
            db.Add(new InterUserCostCenter { UserId = userId, CostCenterId = added, TenantId = request.TenantId });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await CostCentersAsync(userId, ct).ConfigureAwait(false);
    }

    /// <summary>The address a user's forgotten password code goes to.</summary>
    public async Task<UserEmail> EmailAsync(Guid userId, CancellationToken ct)
    {
        Administrator();
        var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("userId", "There is no such user.");
        return new UserEmail(user.Email);
    }

    /// <summary>A user's user name and e-mail address (administrators only), for the Sign-in tab.</summary>
    public async Task<UserSignIn> SignInAsync(Guid userId, CancellationToken ct)
    {
        Administrator();
        var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("userId", "There is no such user.");
        return new UserSignIn(user.Username, user.Email);
    }

    /// <summary>Sets that address; empty removes it.</summary>
    public async Task SetEmailAsync(Guid userId, UserEmail change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        Administrator();
        var address = string.IsNullOrWhiteSpace(change.Email) ? null : change.Email.Trim();
        if (address is not null && Gamma.Erp.Services.Mail.MailService.Address(address) is null)
        {
            throw ValidationException.For("email", $"\"{address}\" is not an e-mail address.");
        }

        var user = await db.Set<User>().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("userId", "There is no such user.");
        if (user.Email != address)
        {
            user.Email = address;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>The language the signed-in user chose at the top of the screens.</summary>
    public async Task SetLanguageAsync(LanguageChoice choice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (choice.Language is { } code && (code.Length != 2 || !code.All(char.IsAsciiLetterLower)))
        {
            throw ValidationException.For("language", $"{code} is not a language.");
        }

        var user = await db.Set<User>().FirstOrDefaultAsync(u => u.Id == request.UserId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("user", "The signed-in user is not there.");
        if (user.Language != choice.Language)
        {
            user.Language = choice.Language;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>The signed-in user's own new password, after the current one.</summary>
    public async Task ChangePasswordAsync(PasswordChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("user", "The signed-in user is not there.");
        if (user.PasswordHash is null || Hasher.VerifyHashedPassword(user, user.PasswordHash, change.Current ?? "") == PasswordVerificationResult.Failed)
        {
            throw ValidationException.For("current", "The current password is wrong.");
        }

        await StoreAsync(db, user.Id, change.New, ct).ConfigureAwait(false);
    }

    /// <summary>An administrator sets a user's password.</summary>
    public async Task ResetPasswordAsync(Guid userId, PasswordReset reset, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reset);
        Administrator();
        if (!await db.Set<User>().AnyAsync(u => u.Id == userId, ct).ConfigureAwait(false))
        {
            throw ValidationException.For("userId", "There is no such user.");
        }

        // A password someone else gave is changed by its user at the next sign-in.
        await StoreAsync(db, userId, reset.New, ct, mustChange: true).ConfigureAwait(false);
    }

    // Only the new hash is kept: no history copy of the old one, and any lockout ends.
    internal static async Task StoreAsync(ErpDbContext db, Guid userId, string password, CancellationToken ct, bool mustChange = false)
    {
        if ((password ?? "").Length < Setup.SetupService.MinPasswordLength)
        {
            throw ValidationException.For("new", $"A password has at least {Setup.SetupService.MinPasswordLength} characters.");
        }

        var entity = db.Model.FindEntityType(typeof(User))!;
        var sql = $"UPDATE \"{entity.GetSchema()}\".\"{entity.GetTableName()}\" SET password_hash = {{0}}, access_failed_count = 0, lockout_end = NULL, must_change_password = {{2}} WHERE id = {{1}} AND active";
        await db.Database.ExecuteSqlRawAsync(sql, [SignInService.Hash(password!), userId, mustChange], ct).ConfigureAwait(false);
    }

    private void Administrator()
    {
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("access", $"{ErrorCodes.NotAllowedError}: only an administrator changes rights and where users work.");
        }
    }

    /// <summary>The entities rights are given on: the elements rows naming a whole entity (its table, no category, type or record), by id.</summary>
    private async Task<Dictionary<Guid, string>> EntitiesAsync(CancellationToken ct) =>
        await db.Set<Elements>().AsNoTracking().Where(e => e.Table != null && e.CategoryId == null && e.TypeId == null && e.RecordId == null)
            .ToDictionaryAsync(e => e.Id, e => e.Table!, ct).ConfigureAwait(false);

    // A category of the entity whose element table this is (its category table, found through the model).
    private async Task<bool> CategoryExistsAsync(string table, Guid categoryId, CancellationToken ct)
    {
        var element = db.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == table && typeof(Element).IsAssignableFrom(e.ClrType));
        var category = element is null ? null : db.Model.GetEntityTypes().FirstOrDefault(e => typeof(Category).IsAssignableFrom(e.ClrType)
            && e.ClrType.Namespace == element.ClrType.Namespace);
        if (category is null)
        {
            return false;
        }

        var set = (IQueryable<Row>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(category.ClrType).Invoke(db, null)!;
        return await set.AnyAsync(c => c.Id == categoryId, ct).ConfigureAwait(false);
    }
}

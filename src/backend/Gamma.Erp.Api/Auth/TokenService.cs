using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Api.Auth;

/// <summary>A personal token as its list shows it: never the token itself.</summary>
public sealed record UserTokenDto(Guid Id, string? Name, string? Prefix, DateTimeOffset CreatedOn, DateTimeOffset? LastUsedOn);

/// <summary>A token just made: the only time the token itself is shown.</summary>
public sealed record NewUserTokenDto(UserTokenDto Token, string Value);

public sealed record NewUserToken(string? Name);

/// <summary>
/// Personal tokens (the founder, 2026-11-04): each user makes and switches off their own; an administrator sees and switches off anyone's. A token is
/// shown once, when it is made, and kept only as its hash.
/// </summary>
public sealed class TokenService(ErpDbContext db, RequestContext request)
{
    /// <summary>The visible start of a token: its prefix and the next four characters.</summary>
    public const int Shown = 7;

    public Task<IReadOnlyList<UserTokenDto>> MineAsync(CancellationToken ct) => ListAsync(request.UserId!.Value, ct);

    public Task<NewUserTokenDto> MakeAsync(NewUserToken made, CancellationToken ct) => MakeForAsync(db, request.TenantId, request.UserId!.Value, made?.Name, ct);

    public Task RevokeMineAsync(Guid id, CancellationToken ct) => RevokeAsync(request.UserId!.Value, id, ct);

    public Task<IReadOnlyList<UserTokenDto>> OfUserAsync(Guid userId, CancellationToken ct)
    {
        Administrator();
        return ListAsync(userId, ct);
    }

    public Task RevokeOfUserAsync(Guid userId, Guid id, CancellationToken ct)
    {
        Administrator();
        return RevokeAsync(userId, id, ct);
    }

    /// <summary>Makes a token for a user (also the first administrator's, made on the server by create-company).</summary>
    internal static async Task<NewUserTokenDto> MakeForAsync(ErpDbContext db, Guid tenantId, Guid userId, string? name, CancellationToken ct)
    {
        var label = string.IsNullOrWhiteSpace(name) ? "Claude" : name.Trim();
        if (label.Length > 60)
        {
            throw ValidationException.For("name", "A token's name has up to 60 characters.");
        }

        var value = TokenSignIn.Make();
        var token = new UserToken { UserId = userId, Name = label, Hash = TokenSignIn.Hash(value), Prefix = value[..Shown], TenantId = tenantId };
        db.Add(token);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new NewUserTokenDto(Dto(token), value);
    }

    private async Task<IReadOnlyList<UserTokenDto>> ListAsync(Guid userId, CancellationToken ct) =>
        [.. (await db.Set<UserToken>().AsNoTracking().Where(t => t.Active && t.UserId == userId).OrderByDescending(t => t.CreatedOn).ToListAsync(ct).ConfigureAwait(false))
            .Select(Dto)];

    private async Task RevokeAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var token = await db.Set<UserToken>().FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId && t.Active, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("id", "There is no such token.");
        token.Active = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static UserTokenDto Dto(UserToken t) => new(t.Id, t.Name, t.Prefix, t.CreatedOn, t.LastUsedOn);

    private void Administrator()
    {
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("access", $"{ErrorCodes.NotAllowedError}: only an administrator sees or switches off another user's tokens.");
        }
    }
}

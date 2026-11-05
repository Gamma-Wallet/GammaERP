using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Gamma.Erp.Api.Auth;

/// <summary>
/// Signing in with a personal token (the founder, 2026-11-04): <c>Authorization: Bearer GE_…</c>. The token names one user; the request then
/// carries the same claims as a sign-in through the sign-in page, so the user's roles and rights apply exactly as on the screens. A token works
/// until it is switched off; a user who is switched off or locked out can't use it.
/// </summary>
public sealed class TokenSignIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ErpDbContext db, TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "GammaToken";

    /// <summary>The start of every personal token.</summary>
    public const string Prefix = "GE_";

    /// <summary>Whether the request carries a personal token (and so is for this handler, not the sign-in page's tokens).</summary>
    public static bool Carries(HttpContext http) =>
        http.Request.Headers.Authorization.ToString() is { } header && header.StartsWith($"Bearer {Prefix}", StringComparison.Ordinal);

    /// <summary>A new token: the prefix and 32 random bytes, URL-safe.</summary>
    public static string Make() => Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The SHA-256 of a token, as it is kept (a token is long and random, so a plain hash is enough).</summary>
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Carries(Context))
        {
            return AuthenticateResult.NoResult();
        }

        var token = Context.Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
        var hash = Hash(token);
        // Before the corporate is known, as the sign-in page finds a user by name.
        var found = await (from t in db.Set<UserToken>().AsNoTracking() join u in db.Set<User>().AsNoTracking() on t.UserId equals u.Id
                           where t.Hash == hash && t.Active && u.Active select new { Token = t, User = u }).FirstOrDefaultAsync(Context.RequestAborted).ConfigureAwait(false);
        if (found is null || found.User.LockoutEnd is { } end && end > clock.GetUtcNow())
        {
            return AuthenticateResult.Fail("The token is not valid.");
        }

        // When it was last used, at most every five minutes, without a history copy (like the sign-in counters: not business data).
        if (found.Token.LastUsedOn is not { } last || clock.GetUtcNow() - last > TimeSpan.FromMinutes(5))
        {
            var entity = db.Model.FindEntityType(typeof(UserToken))!;
            var sql = $"UPDATE \"{entity.GetSchema()}\".\"{entity.GetTableName()}\" SET last_used_on = {{0}} WHERE id = {{1}}";
            await db.Database.ExecuteSqlRawAsync(sql, [clock.GetUtcNow(), found.Token.Id], Context.RequestAborted).ConfigureAwait(false);
        }

        var identity = new ClaimsIdentity([new Claim(Claims.Subject, found.User.Id.ToString()), new Claim(AuthEndpoints.TenantClaim, found.User.TenantId.ToString()),
            new Claim(Claims.Name, found.User.Username ?? string.Empty)], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

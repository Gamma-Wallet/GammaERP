using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Api.Auth;

/// <summary>
/// Checks a user's name and password. Passwords are only ever kept as hashes; repeated wrong passwords lock
/// the user for a while. Every sign-in, sign-out and wrong password of a known user is recorded in tbl_login.
/// </summary>
public sealed class SignInService(ErpDbContext db, TenantContext tenant, TimeProvider clock)
{
    /// <summary>Wrong passwords in a row before the user is locked.</summary>
    public const int MaxFailures = 5;

    /// <summary>How long a locked user waits.</summary>
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);

    private static readonly PasswordHasher<User> Hasher = new();

    /// <summary>A user name as it is kept and compared: trimmed, lower case.</summary>
    public static string Normalize(string username) => (username ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>The hash to keep for a password (PBKDF2 with a random salt).</summary>
    public static string Hash(string password) => Hasher.HashPassword(null!, password);

    /// <summary>Whether a text is the one a hash was made from.</summary>
    public static bool Matches(string hash, string text) => Hasher.VerifyHashedPassword(null!, hash, text) != PasswordVerificationResult.Failed;

    public sealed record Result(User? User, string? Error);

    public async Task<Result> CheckAsync(string username, string password, CancellationToken ct)
    {
        // Before the corporate is known: the user is found by name across the system (the name is unique).
        // Names are kept normalized (Normalize) and compared exactly: no LIKE, so no wildcard ever matches another user.
        var name = Normalize(username);
        var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Username == name, ct).ConfigureAwait(false);
        if (user?.PasswordHash is null)
        {
            Hasher.VerifyHashedPassword(null!, Hash("timing"), password); // the same work as for a real user, so names can't be guessed by time
            return new Result(null, "The name or the password is wrong.");
        }

        if (user.LockoutEnd is { } end && end > clock.GetUtcNow())
        {
            return new Result(null, "Too many wrong passwords: try again later.");
        }

        var table = $"\"{db.Model.FindEntityType(typeof(User))!.GetSchema()}\".\"{db.Model.FindEntityType(typeof(User))!.GetTableName()}\"";
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            // The counters are kept without a history copy (like a type's next code): they are not business data.
            var failed = user.AccessFailedCount + 1;
            DateTimeOffset? lockUntil = failed >= MaxFailures ? clock.GetUtcNow() + Lockout : null;
            var sql = $"UPDATE {table} SET access_failed_count = {{0}}, lockout_end = {{1}} WHERE id = {{2}}";
            await db.Database.ExecuteSqlRawAsync(sql, [failed >= MaxFailures ? 0 : failed, lockUntil!, user.Id], ct).ConfigureAwait(false);
            await RecordAsync(user, signedIn: false, ct, failed: true).ConfigureAwait(false);
            return new Result(null, "The name or the password is wrong.");
        }

        var reset = $"UPDATE {table} SET access_failed_count = 0, lockout_end = NULL WHERE id = {{0}}";
        await db.Database.ExecuteSqlRawAsync(reset, [user.Id], ct).ConfigureAwait(false);
        await RecordAsync(user, signedIn: true, ct).ConfigureAwait(false);
        return new Result(user, null);
    }

    /// <summary>Records a sign-in, a sign-out or a wrong password.</summary>
    public async Task RecordAsync(User user, bool signedIn, CancellationToken ct, bool failed = false)
    {
        ArgumentNullException.ThrowIfNull(user);
        tenant.Set(user.TenantId, user.Id);
        db.Add(new Login { UserId = user.Id, InOut = signedIn, Failed = failed, OwnerId = user.OwnerId });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

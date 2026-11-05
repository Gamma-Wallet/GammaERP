using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>A user.</summary>
[Table("tbl_user")]
public sealed class User : Element
{
    /// <summary>The name the user signs in with, unique across the whole system: sign-in happens before the corporate is known.</summary>
    public string? Username { get; set; }

    /// <summary>Where a forgotten password's code goes (new here); none, and only an administrator can give the user a new password.</summary>
    public string? Email { get; set; }

    /// <summary>The language the user chose at the top of the screens; empty, or not one of the company's two, is English, the default.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// The password's hash (PBKDF2 with a random salt, the ASP.NET Core password hasher). The password itself is never stored.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>Wrong passwords in a row since the last good sign-in.</summary>
    public int AccessFailedCount { get; set; }

    /// <summary>Until when sign-in is refused after too many wrong passwords.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>
    /// The password was given by someone else (create-company, or an administrator's reset): the sign-in page asks for a new one before anything else
    /// (the founder, 2026-11-04). Personal tokens still work.
    /// </summary>
    public bool MustChangePassword { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>
/// A user's personal token (the founder, 2026-11-04): Claude, or a script, signs in with it as that user, with that user's
/// rights. Only its hash is kept; the token is shown once, when it is made. It works until it is switched off (revoked).
/// </summary>
[Table("tbl_user_token")]
public sealed class UserToken : Row
{
    public Guid UserId { get; set; }

    /// <summary>What it is for, as its user names it (e.g. "Claude Desktop").</summary>
    public string? Name { get; set; }

    /// <summary>The SHA-256 of the token, in hexadecimal: the token itself is never kept.</summary>
    public string Hash { get; set; } = "";

    /// <summary>The token's first characters, to tell tokens apart on the screen.</summary>
    public string? Prefix { get; set; }

    public DateTimeOffset? LastUsedOn { get; set; }
}

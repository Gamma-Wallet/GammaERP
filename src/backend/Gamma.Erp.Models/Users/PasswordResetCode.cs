using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>A code to choose a new password, sent to a user's e-mail address (new here): it works once, until it expires or too many wrong tries. Only its hash is kept.</summary>
[Table("tbl_password_reset")]
public sealed class PasswordResetCode : Row
{
    /// <summary>The user the code is for.</summary>
    public Guid UserId { get; set; }

    /// <summary>The code's hash (PBKDF2 with a random salt, as a password's: a short code must not be recoverable from it).</summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>When the code stops working.</summary>
    public DateTimeOffset ExpiresOn { get; set; }

    /// <summary>Wrong tries so far.</summary>
    public int Tries { get; set; }

    /// <summary>When it was used, replaced by a newer code, or closed after too many wrong tries.</summary>
    public DateTimeOffset? UsedOn { get; set; }
}

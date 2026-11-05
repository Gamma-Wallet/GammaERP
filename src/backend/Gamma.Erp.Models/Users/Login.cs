using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>A sign-in, sign-out or failed sign-in of a user; when is <see cref="Row.CreatedOn"/>.</summary>
[Table("tbl_login")]
public sealed class Login : Row
{
    /// <summary>The user who signed in or out.</summary>
    public Guid UserId { get; set; }

    /// <summary>True = signed in, false = signed out.</summary>
    public bool InOut { get; set; }

    /// <summary>A wrong password for this user: nobody signed in.</summary>
    public bool Failed { get; set; }
}

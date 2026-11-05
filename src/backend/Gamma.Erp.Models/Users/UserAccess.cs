using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>A user who may see a user.</summary>
[Table("tbl_user_access")]
public sealed class UserAccess : ElementAccess;

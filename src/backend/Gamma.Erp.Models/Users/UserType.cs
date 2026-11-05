using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>A type of user.</summary>
[Table("tbl_user_type")]
public sealed class UserType : ElementType;

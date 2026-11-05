using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>The value of a property for a user.</summary>
[Table("tbl_user_info")]
public sealed class UserInfo : ElementInfo;

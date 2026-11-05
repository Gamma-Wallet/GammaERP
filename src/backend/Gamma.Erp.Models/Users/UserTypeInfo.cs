using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>A property of a user type.</summary>
[Table("tbl_user_type_info")]
public sealed class UserTypeInfo : TypeInfo;

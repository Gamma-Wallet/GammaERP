using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>A choice of a user list property.</summary>
[Table("tbl_user_type_info_ch")]
public sealed class UserTypeInfoChoice : TypeInfoChoice;

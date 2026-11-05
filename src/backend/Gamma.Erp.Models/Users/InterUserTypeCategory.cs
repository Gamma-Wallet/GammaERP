using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>A user type in a user category.</summary>
[Table("tbl_inter_user_type_category")]
public sealed class InterUserTypeCategory : InterTypeCategory;

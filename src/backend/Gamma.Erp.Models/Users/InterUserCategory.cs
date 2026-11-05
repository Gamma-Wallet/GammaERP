using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Users;

/// <summary>A user in a user category.</summary>
[Table("tbl_inter_user_category")]
public sealed class InterUserCategory : InterElementCategory;

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Employees;

/// <summary>A employee in a employee category.</summary>
[Table("tbl_inter_employee_category")]
public sealed class InterEmployeeCategory : InterElementCategory;

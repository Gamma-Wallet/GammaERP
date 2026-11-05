using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Employees;

/// <summary>A category of employees.</summary>
[Table("tbl_employee_category")]
public sealed class EmployeeCategory : Category;

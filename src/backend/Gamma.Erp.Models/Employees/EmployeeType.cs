using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Employees;

/// <summary>A type of employee.</summary>
[Table("tbl_employee_type")]
public sealed class EmployeeType : ElementType;

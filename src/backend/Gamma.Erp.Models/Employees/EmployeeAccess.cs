using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Employees;

/// <summary>A user who may see a employee.</summary>
[Table("tbl_employee_access")]
public sealed class EmployeeAccess : ElementAccess;

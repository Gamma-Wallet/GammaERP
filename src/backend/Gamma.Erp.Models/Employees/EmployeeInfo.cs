using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Employees;

/// <summary>The value of a property for a employee.</summary>
[Table("tbl_employee_info")]
public sealed class EmployeeInfo : ElementInfo;

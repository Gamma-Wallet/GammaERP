using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Employees;

/// <summary>A property of a employee type.</summary>
[Table("tbl_employee_type_info")]
public sealed class EmployeeTypeInfo : TypeInfo;

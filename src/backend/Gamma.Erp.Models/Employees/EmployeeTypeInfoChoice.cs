using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Employees;

/// <summary>A choice of a employee list property.</summary>
[Table("tbl_employee_type_info_ch")]
public sealed class EmployeeTypeInfoChoice : TypeInfoChoice;

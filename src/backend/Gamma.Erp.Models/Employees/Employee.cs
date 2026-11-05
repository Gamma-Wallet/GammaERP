using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Employees;

/// <summary>A employee.</summary>
[Table("tbl_employee")]
public sealed class Employee : Element;

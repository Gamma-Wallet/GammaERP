using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Charts;

/// <summary>A account of the chart.</summary>
[Table("tbl_chart")]
public sealed class Chart : Element;

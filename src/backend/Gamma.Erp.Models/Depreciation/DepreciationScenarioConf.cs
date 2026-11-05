using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Depreciation;

[Table("tbl_depreciation_scenario_conf")]
public sealed class DepreciationScenarioConf : Row
{
    public Guid? DepreciationScenarioId { get; set; }

    public Guid? ChartId { get; set; }

    public string? ChartName { get; set; }

    public bool Side { get; set; }

    public string? SideValue { get; set; }
}

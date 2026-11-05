using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Posting;

[Table("tbl_scenario_conf")]
public sealed class ScenarioConf : Row
{
    public Guid? ScenarioId { get; set; }

    public Guid? ChartId { get; set; }

    public string? ChartName { get; set; }

    public bool Side { get; set; }

    public string? SideValue { get; set; }

    /// <summary>Old value: the old number of the amount (1 value, 2 discount, 3 VAT…), kept for reference; the link is <see cref="ValueElementsId"/>.</summary>
    public int? Value { get; set; }

    public string? ValueName { get; set; }

    /// <summary>
    /// The amount the line takes: a "Transaction amount" row of tbl_elements, which names the column of tbl_transaction it reads. New: the
    /// </summary>
    public Guid? ValueElementsId { get; set; }

    /// <summary>
    /// Where the line is written: 1 for the document's from side, 2 for its to side (only when that side has an element), 3 on the
    /// transaction's element, which is then the account (journal account lines; <see cref="ChartId"/> is empty); 4 / 5 the account the from /
    /// to side's element holds (a cash desk's or bank's account property); 6 the account the transaction's element holds (a cash desk or bank
    /// line).
    /// </summary>
    public int? Place { get; set; }

    /// <summary>The cost centre the entry carries: 1 the from side's element, 2 the to side's, empty for none. New, as <see cref="Place"/>.</summary>
    public int? CostCenterPlace { get; set; }
}

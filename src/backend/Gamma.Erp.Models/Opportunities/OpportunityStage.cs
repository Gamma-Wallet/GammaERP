using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Opportunities;

/// <summary>A stage of the company's pipeline: configuration.</summary>
[Table("tbl_opportunity_stage")]
public sealed class OpportunityStage : Row
{
    public TranslatedText Name { get; set; } = new();

    /// <summary>The chance of winning at this stage, in percent.</summary>
    public decimal Chance { get; set; }

    public decimal SortOrder { get; set; }

    /// <summary>0 open, 1 won, 2 lost.</summary>
    public int Outcome { get; set; }
}

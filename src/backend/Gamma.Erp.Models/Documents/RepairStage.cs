using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A stage of the repair board: configuration.</summary>
[Table("tbl_repair_stage")]
public sealed class RepairStage : Row
{
    public TranslatedText Name { get; set; } = new();

    public decimal SortOrder { get; set; }

    /// <summary>The job is over: collected or refused.</summary>
    public bool IsFinal { get; set; }
}

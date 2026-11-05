using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A repair on the board, from the intake document on (the founder, 2026-11-02). The item is the client's: it is never stock.</summary>
[Table("tbl_repair_job")]
public sealed class RepairJob : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>The intake document.</summary>
    public Guid IntakeId { get; set; }

    public Guid RepairStageId { get; set; }

    /// <summary>The technician, from the staff register.</summary>
    public Guid? TechnicianId { get; set; }
}

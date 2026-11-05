using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Opportunities;

/// <summary>A sale being worked on with a client or a lead, moved from stage to stage by hand. New here.</summary>
[Table("tbl_opportunity")]
public sealed class Opportunity : Element
{
    /// <summary>The client or lead, a partner.</summary>
    public Guid? PartnerId { get; set; }

    /// <summary>Its stage (<see cref="OpportunityStage"/>).</summary>
    public Guid? StageId { get; set; }

    /// <summary>What it may bring, in <see cref="CurrencyId"/>.</summary>
    public decimal? Amount { get; set; }

    public Guid? CurrencyId { get; set; }

    /// <summary>When it is expected to close.</summary>
    public DateOnly? ExpectedClose { get; set; }

    /// <summary>The user working on it.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Why it was lost: a lost stage needs one.</summary>
    public string? LostReason { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

/// <summary>A closed period of the company that owns the row: nothing posts on a day up to this one, reversals included.</summary>
[Table("tbl_posting_lock")]
public sealed class PostingLock : Row
{
    /// <summary>The last closed day.</summary>
    public DateOnly LockedUntil { get; set; }
}

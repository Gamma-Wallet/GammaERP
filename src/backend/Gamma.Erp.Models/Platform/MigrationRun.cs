using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

/// <summary>A file imported into a migration step: who and when are the row's own columns.</summary>
[Table("tbl_migration_run")]
public sealed class MigrationRun : Row
{
    public Guid MigrationItemId { get; set; }

    public string? FileName { get; set; }

    public int Added { get; set; }

    public int Updated { get; set; }

    /// <summary>The journal sheet an opening balances file posted.</summary>
    public Guid? DocumentId { get; set; }
}

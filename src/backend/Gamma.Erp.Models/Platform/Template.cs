using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

[Table("tbl_template")]
public sealed class Template : Row
{
    public Guid? DocumentTypeId { get; set; }

    public string? ShortName { get; set; }

    public string? LongName { get; set; }

    public bool Pos { get; set; }

    public string? Body { get; set; }

    public string? PrinterName { get; set; }

    public int? Copies { get; set; }
}

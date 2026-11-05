using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

[Table("tbl_report")]
public sealed class Report : Row
{
    public TranslatedText? Name { get; set; }

    public string? Code { get; set; }

    public bool IsElement { get; set; }

    public int? Conf { get; set; }

    public string? ElementName { get; set; }

    public string? ElementTable { get; set; }

    public string? Table1 { get; set; }

    public string? Table2 { get; set; }
}

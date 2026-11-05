using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

[Table("tbl_setting_type")]
public sealed class SettingType : Row
{
    public bool IsElement { get; set; }

    public string? Code { get; set; }

    public int? CodeStart { get; set; }

    public int? CodeEnd { get; set; }

    public int? CodeCurrent { get; set; }

    public TranslatedText? Name { get; set; }

    public TranslatedText? Description { get; set; }

    public double SortOrder { get; set; }
}

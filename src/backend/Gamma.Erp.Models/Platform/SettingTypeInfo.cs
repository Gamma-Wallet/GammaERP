using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

[Table("tbl_setting_type_info")]
public sealed class SettingTypeInfo : Row
{
    public Guid? TypeId { get; set; }

    public TranslatedText? Name { get; set; }

    public TranslatedText? Description { get; set; }

    public bool Required { get; set; }

    public int? Kind { get; set; }

    public double SortOrder { get; set; }

    public Guid? ElementsId { get; set; }

    public bool IsFile { get; set; }
}

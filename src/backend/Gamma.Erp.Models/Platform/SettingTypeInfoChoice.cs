using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Platform;

[Table("tbl_setting_type_info_ch")]
public sealed class SettingTypeInfoChoice : Row
{
    public Guid? TypeInfoId { get; set; }

    public TranslatedText? Name { get; set; }

    public TranslatedText? Description { get; set; }
}

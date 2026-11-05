using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Depreciation;

[Table("tbl_depreciation_method")]
public sealed class DepreciationMethod : Row
{
    public TranslatedText? Name { get; set; }

    public TranslatedText? Description { get; set; }

    public string? Formula { get; set; }
}

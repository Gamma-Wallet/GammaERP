using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Depreciation;

[Table("tbl_depreciation_book")]
public sealed class DepreciationBook : Row
{
    public TranslatedText? Name { get; set; }

    public TranslatedText? Description { get; set; }

    public Guid? MethodId { get; set; }

    public Guid? ChartCategoryId { get; set; }

    public bool Posts { get; set; }
}

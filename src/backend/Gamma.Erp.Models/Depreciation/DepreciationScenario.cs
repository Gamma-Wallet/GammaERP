using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Depreciation;

[Table("tbl_depreciation_scenario")]
public sealed class DepreciationScenario : Row
{
    public TranslatedText? Name { get; set; }

    public TranslatedText? Description { get; set; }

    public Guid? DepreciationBookId { get; set; }

    public Guid? ElementsId { get; set; }

    /// <summary>
    /// The asset's useful life in months that registering an asset of this scope in this book proposes (a country's rate as a life, e.g.
    /// 20 % a year → 60). Null: none proposed (2026-10-29).
    /// </summary>
    public int? Months { get; set; }

    /// <summary>The linear percentage a year it proposes.</summary>
    public decimal? LinearPercentage { get; set; }

    /// <summary>The declining percentage a year it proposes.</summary>
    public decimal? DecliningPercentage { get; set; }
}

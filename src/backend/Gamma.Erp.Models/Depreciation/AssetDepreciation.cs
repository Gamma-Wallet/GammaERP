using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Depreciation;

[Table("tbl_depreciation")]
public sealed class AssetDepreciation : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid? AssetValueId { get; set; }

    public Guid? CostCenterId { get; set; }

    public DateOnly? DepreciationStartDate { get; set; }

    public DateOnly? DepreciationDate { get; set; }

    public decimal? DepreciationValue { get; set; }

    public decimal? BookValue { get; set; }

    public bool Posted { get; set; }

    public Guid? DocumentId { get; set; }

    public decimal? PostedValue { get; set; }
}

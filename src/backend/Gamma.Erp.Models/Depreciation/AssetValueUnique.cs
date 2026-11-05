using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Depreciation;

[Table("tbl_asset_value_unique")]
public sealed class AssetValueUnique : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid? ProductSpecificUnitId { get; set; }

    public Guid? BookId { get; set; }

    public Guid? MethodId { get; set; }

    public DateOnly? EndDate { get; set; }

    public decimal? LinearPercentage { get; set; }

    public decimal? DecliningPercentage { get; set; }

    public DateOnly? DisposeDate { get; set; }

    public bool Disposed { get; set; }
}

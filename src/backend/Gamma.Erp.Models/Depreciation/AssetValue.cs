using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Depreciation;

[Table("tbl_asset_value")]
public sealed class AssetValue : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid? ProductSpecificUnitId { get; set; }

    public Guid? DocumentId { get; set; }

    public Guid? BookId { get; set; }

    public Guid? MethodId { get; set; }

    public DateOnly? StartDate { get; set; }

    public decimal? Years { get; set; }

    public decimal? Months { get; set; }

    public decimal? InitialValue { get; set; }

    /// <summary>The account holding its cost, when it came in through the migration.</summary>
    public Guid? AssetChartId { get; set; }

    /// <summary>The account holding its depreciation, when it came in through the migration.</summary>
    public Guid? DepreciationChartId { get; set; }

    /// <summary>The day it was sold: it no longer depreciates. A reversal of the sale clears it.</summary>
    /// <summary>The cost centre its depreciation goes to, wherever the asset is, e.g. when it sits at clients' houses; empty: the place that holds it.</summary>
    public Guid? DefaultCostCenterId { get; set; }

    public DateOnly? SoldOn { get; set; }

    /// <summary>The sale that sold it.</summary>
    public Guid? SoldDocumentId { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

[Table("tbl_vat_group")]
public sealed class VatGroup : Row
{
    public string? Code { get; set; }

    public TranslatedText? Name { get; set; }

    public TranslatedText? Description { get; set; }

    /// <summary>Old rate.</summary>
    public decimal? Rate { get; set; }

    public bool Depreciation { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

/// <summary>A type of product.</summary>
[Table("tbl_product_type")]
public sealed class ProductType : ElementType
{
    /// <summary>Each unit of its products has a serial number: a line moving them needs one per unit.</summary>
    public bool TracksSerial { get; set; }

    /// <summary>Its products move lot by lot: a line moving them names its lot.</summary>
    public bool TracksLot { get; set; }

    /// <summary>A lot past its expiry date can't go to a client.</summary>
    public bool RefusesExpired { get; set; } = true;

    /// <summary>The VAT group the lines of its products start with, unless the product names its own.</summary>
    public Guid? VatGroupId { get; set; }

    /// <summary>Its products are fixed assets: they are registered in depreciation books and depreciated. Set on each type, not inherited.</summary>
    public bool FixedAsset { get; set; }
}

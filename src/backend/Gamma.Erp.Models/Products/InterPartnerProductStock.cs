using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Products;

[Table("tbl_inter_partner_product_stock")]
public sealed class InterPartnerProductStock : Row
{
    /// <summary>The company the row belongs to.</summary>
    public Guid CompanyId { get; set; }

    public Guid? PartnerId { get; set; }

    public Guid? DocumentId { get; set; }

    public Guid? ProductSpecificUnitId { get; set; }

    public decimal? Quantity { get; set; }

    public decimal? DocumentQuantity { get; set; }
}

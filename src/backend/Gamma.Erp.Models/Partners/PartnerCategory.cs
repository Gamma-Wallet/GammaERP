using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Partners;

/// <summary>A category of partners, e.g. Clients, Suppliers.</summary>
[Table("tbl_partner_category")]
public sealed class PartnerCategory : Category;

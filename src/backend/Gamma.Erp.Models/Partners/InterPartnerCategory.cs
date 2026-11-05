using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Partners;

/// <summary>A partner in a partner category, e.g. in Clients and in Suppliers.</summary>
[Table("tbl_inter_partner_category")]
public sealed class InterPartnerCategory : InterElementCategory;

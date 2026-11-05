using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Partners;

/// <summary>A partner type in a partner category, e.g. SME in Clients and in Suppliers.</summary>
[Table("tbl_inter_partner_type_category")]
public sealed class InterPartnerTypeCategory : InterTypeCategory;

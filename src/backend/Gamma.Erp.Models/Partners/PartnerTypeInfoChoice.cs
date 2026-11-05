using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Partners;

/// <summary>A choice of a partner list property, e.g. SHPK, SHA of "company type".</summary>
[Table("tbl_partner_type_info_ch")]
public sealed class PartnerTypeInfoChoice : TypeInfoChoice;

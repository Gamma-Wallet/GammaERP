using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Partners;

/// <summary>The value of a property for a partner.</summary>
[Table("tbl_partner_info")]
public sealed class PartnerInfo : ElementInfo;

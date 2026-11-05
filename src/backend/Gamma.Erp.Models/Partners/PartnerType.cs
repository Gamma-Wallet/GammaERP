using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Partners;

/// <summary>A type of partner, e.g. Unknown, Person, SME, LTD.</summary>
[Table("tbl_partner_type")]
public sealed class PartnerType : ElementType;

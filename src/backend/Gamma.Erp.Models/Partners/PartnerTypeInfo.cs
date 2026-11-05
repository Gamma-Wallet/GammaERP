using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Partners;

/// <summary>A property of a partner type, e.g. tax number, credit limit.</summary>
[Table("tbl_partner_type_info")]
public sealed class PartnerTypeInfo : TypeInfo;

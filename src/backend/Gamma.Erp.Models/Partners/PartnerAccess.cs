using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Partners;

/// <summary>A user who may see a partner.</summary>
[Table("tbl_partner_access")]
public sealed class PartnerAccess : ElementAccess;

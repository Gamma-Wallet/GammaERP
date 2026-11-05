using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Partners;

/// <summary>A partner: a client, a supplier or both, by its categories.</summary>
[Table("tbl_partner")]
public sealed class Partner : Element
{
    /// <summary>
    /// The company or group of the same group this partner stands for (the founder, 2026-10-31: what passes between a group and its companies
    /// goes by documents, so each is the other's client and supplier); empty for every other partner.
    /// </summary>
    public Guid? GroupMemberId { get; set; }

    /// <summary>The days the partner has to pay: an invoice falls due that long after its date.</summary>
    public int? PaymentDays { get; set; }
}

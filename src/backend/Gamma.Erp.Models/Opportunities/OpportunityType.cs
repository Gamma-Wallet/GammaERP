using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Opportunities;

[Table("tbl_opportunity_type")]
public sealed class OpportunityType : ElementType;

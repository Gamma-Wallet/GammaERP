using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Opportunities;

[Table("tbl_inter_opportunity_category")]
public sealed class InterOpportunityCategory : InterElementCategory;

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Posting;

[Table("tbl_scenario")]
public sealed class Scenario : Row
{
    public TranslatedText? Name { get; set; }

    public TranslatedText? Description { get; set; }

    public Guid? DocumentTypeTransactionTypeId { get; set; }

    public Guid? ElementsId { get; set; }
}

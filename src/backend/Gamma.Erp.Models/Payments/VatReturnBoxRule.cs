using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Payments;

/// <summary>
/// Which document lines a VAT return box takes: those of posted documents of a category or a type (and of the reversal types reversing them,
/// which count negative), of a line type, in a VAT group; an empty part takes any. A line goes into the box of the most specific rule that
/// takes it, per side and amount (a document type counts before a line type, before a category and a VAT group), so a customs declaration's
/// lines go to the import boxes, not to the purchases ones.
/// </summary>
[Table("tbl_vat_return_box_rule")]
public sealed class VatReturnBoxRule : Row
{
    public Guid BoxId { get; set; }

    public Guid? DocumentCategoryId { get; set; }

    public Guid? DocumentTypeId { get; set; }

    public Guid? TransactionTypeId { get; set; }

    public Guid? VatGroupId { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using Gamma.Erp.Models.Base;

namespace Gamma.Erp.Models.Documents;

[Table("tbl_document_type_conf_from")]
public sealed class DocumentTypeConfFrom : Row
{
    public Guid? DocumentTypeId { get; set; }

    public Guid? ElementsId { get; set; }

    /// <summary>
    /// The amount of each transaction that goes into this side's balance (partner, cash desk / warehouse, employee): a "Transaction amount"
    /// row of tbl_elements; credit on the from side, debit on the to side; empty for none.
    /// </summary>
    public Guid? BalanceValueElementsId { get; set; }

    /// <summary>
    /// The amount that goes into the product's value (tbl_product_balance) for product lines on this side: debit on the to side, with the
    /// purchase price recorded; credit on the from side. Empty for none, as <see cref="BalanceValueElementsId"/>.
    /// </summary>
    public Guid? ProductValueElementsId { get; set; }

    /// <summary>
    /// What posting does to the cost layers (tbl_product_cost_layer) of the stock this side moves: 0 none (a client buying, a supplier
    /// selling), 1 take from the side's oldest layers (a sale, a transfer out), 2 make a layer at the line's product value (a purchase bill),
    /// 3 receive the layers the from side took (a transfer in), 4 put back the layers the reversed line took (a reversal of a sale), 5 take
    /// off the reversed line's own layer (a reversal of a purchase), 6 make a layer at what the document's raw materials cost (a production),
    /// 7 receive what the from side took of the same product converted into the line's unit (a unit split). New (the founder's rule, 2026-10-30).
    /// </summary>
    public int CostLayers { get; set; }
}

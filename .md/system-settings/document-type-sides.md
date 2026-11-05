# Document type sides

Every document has a From side and a To side. These rows say what each side may be, and what posting does to it.

**Where:** System settings › Document types › a type › From / To. **Who:** an administrator changes rows. The tables work as in [Configuration screens](configuration-screens.md).

Configuration: `tbl_document_type_conf_from`, `tbl_document_type_conf_to`. Models `document-type-from`, `document-type-to`. The base pack has 86 rows on the From side.

| Field | Column | Meaning |
|---|---|---|
| Elements | `ElementsId` | The scope the side picks from: a row of `tbl_elements`, such as cost centres or clients and suppliers. |
| Balance value elements | `BalanceValueElementsId` | The amount of each line that goes into this side's balance: credit on the From side, debit on the To side. Empty: none. |
| Product value elements | `ProductValueElementsId` | The amount that goes into the product's value, for product lines on this side: debit on the To side, with the purchase price recorded; credit on the From side. Empty: none. |
| Cost layers | `CostLayers` | What posting does to the cost layers of the stock this side moves. |

Cost layers:

| Value | Name on the screen | Example |
|---|---|---|
| 0 | None | A client buying, a supplier selling. |
| 1 | Take (oldest first) | A sale, a transfer out. |
| 2 | Make (a purchase) | A purchase bill, at the line's product value. |
| 3 | Receive (a transfer) | A transfer in: the layers the From side took. |
| 4 | Put back (a reversal) | A reversal of a sale. |
| 5 | Take off (a reversal) | A reversal of a purchase. |
| 6 | Make from used | A production, at what its raw materials cost. |
| 7 | Receive converted | A unit split. |
| 8 | Receive shared by average cost | A disassembly: what was taken, shared among the lines by quantity × average cost. |

## Actions

### Add a side row

Steps:

1. Choose the document type and open **From / To**.
2. Under **From** or **To** press **Add**.
3. Choose the fields.
4. Press **Save**.

Result: the document editor offers that scope on this side. The next posting of this type uses the amounts and the cost layers.

Refused when: as in [Configuration screens](configuration-screens.md).

API: `GET /config/document-type-from?documentTypeId=`, `POST /config/document-type-from`. The same for `document-type-to`.

### Change or switch off a side row

Steps:

1. Press the row.
2. Change it and press **Save**, or press **Switch off...**

Result: the engine reads the new row from the next document. Posted documents stay as they are.

API: `POST /config/document-type-from` with `id`, `DELETE /config/document-type-from/{id}`. The same for `document-type-to`.

## Related

- [Document types](document-types.md)
- [Document type transactions](document-type-transactions.md)
- [Posting](../documents/posting.md)

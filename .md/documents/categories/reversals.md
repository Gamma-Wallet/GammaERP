# Reversals

The documents that correct posted documents: credit notes and reversal documents. They are made only by reversing the original, and are read only.

**Where:** Documents › Reversals. **Who:** read on the category to see them. To make one, see [reversing](../reversing.md).

## Types in the base pack

| Reversal type | Reverses |
|---------------|----------|
| Credit note, goods sold | Sales invoice, goods |
| Credit note, services sold | Sales invoice, services |
| Credit note, goods bought | Purchase invoice, goods |
| Credit note, services bought | Purchase invoice, services |
| Reversal of payment | Payment |
| Reversal of receipt | Receipt |
| Reversal of prepayment | Prepayment |
| Reversal of advance | Advance |
| Reversal of employee advance | Employee advance |
| Reversal of unit split | Unit split |
| Reversal of customs declaration | Customs declaration |
| Reversal of customs declaration, assets | Customs declaration, assets |
| Reversal of goods transfer note | Goods transfer note |
| Reversal of goods transfer, cost centre to staff | Goods transfer, cost centre to staff |
| Reversal of goods transfer, staff to cost centre | Goods transfer, staff to cost centre |
| Reversal of goods transfer, cost centre to client | Goods transfer, cost centre to client |
| Reversal of goods transfer, client to cost centre | Goods transfer, client to cost centre |
| Reversal of goods transfer, staff to client | Goods transfer, staff to client |
| Reversal of journal sheet | Journal sheet, and a twin for the recurring sheet |
| Reversal of depreciation sheet | Depreciation sheet, and a twin for the recurring sheet |

Under the reversal sheets sit reversal row types, one per journal row type. The reversals of point-of-sale receipts and money (`POS_REVERSAL`, `POS_REFUND`) are listed in [Sales](sales.md) and [Cash desk](cash-desk.md).

A type is reversed by one reversal type at most: the database keeps it so. A type with none can't be reversed. Set one under System settings › Document types › the reversal type › General: tick "This type is a reversal" and pick "It reverses".

## Actions

### See a reversal

Steps:
1. Open Documents › Reversals, or the Linked documents tab of the original.

Result: the State column shows "Reversal of {number}". The editor is read only, and its Linked documents tab leads back to the original.

API: `GET documents?category={id}`, `GET documents/{id}` gives `reversedDocumentId` and `reversedDocumentCode`.

### Print or e-mail a credit note

Steps:
1. Open it and press **Print** or **E-mail**. See [other actions](../other-actions.md).

Result: the PDF says what it reverses.

## Not possible

- The **New** button shows in this category, but saving a document of a reversal type is refused: `ReversalError: a reversal is made only by reversing the document it reverses.`
- A reversal is not changed, switched off, repeated or reversed.

## Related

- [Reversing a posted document](../reversing.md)

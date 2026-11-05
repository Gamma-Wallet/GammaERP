# Selling an asset

Selling a fixed asset is a normal sale of its product, at the asset's actual value. There is no separate feature or screen.

**Where:** Documents › Sales, with a document type that sells assets. **Who:** the rights of that document type.

The setting is `tbl_document_type.sells_assets`. In the starter pack it is on for Sales invoice, goods; Sales invoice, services; and Point-of-sale receipt. A transfer to a client has it off: the asset stays the company's. An administrator changes it in System settings › Document types › the type › General, box "Its posted documents sell the fixed assets on their lines" (`PUT /documents/types/{id}/settings`). See [Document types](../system-settings/document-types.md).

## Actions

### Sell an asset

Steps:
1. Make a sales document of a type that sells assets.
2. Add a line with the asset's product and unit. The price proposed is what is left of the asset in the book that posts. It is proposed only on a type that sells assets, so a transfer to a client does not propose it. You may change it.
3. Save and post the document as usual.

Result, when it posts:
- Each line of the asset gets its figures, in the company's currency: cost (`asset_cost_lcu`), depreciation so far (`asset_depreciated_lcu`) and book value (`asset_book_value_lcu`). Scenario lines can read these three through the "Transaction asset cost", "Transaction asset depreciation" and "Transaction asset book value" amounts. The gain or loss of a sale, and taking out cost and depreciation, is configuration: the company adds those scenario lines under Accounting setup › Scenario lines. No starter scenario line reads the three amounts.
- The entries are written by the line's posting scenario, like any sale.
- The asset's values in every depreciation book, the Tax book too, get their sold date and sale document (`sold_on`, `sold_document_id`). They leave the runs of every book.
- The Assets tab shows the asset as Disposed (no longer in use), and Remaining asset value no longer counts it. A sold asset shows no Mark disposed or Back in use button: its sale closed it.
- Stock moves as on any sale.

Refused when: anything that refuses a sale, such as `NoScenarioError` when no scenario fits the line, or `StockError`.

API: `GET /documents/line-proposal` for the proposed price, `POST /documents`, `POST /documents/{id}/post`.

### Reverse the sale

Steps:
1. Reverse the sale the usual way.

Result: the reversal line carries the asset's figures, and the asset comes back into depreciation in every book: its sold date and sale document are cleared. It is no longer shown as Disposed and counts in Remaining asset value again.

API: `POST /documents/{id}/reverse`.

## Related

- [assets-at-clients.md](assets-at-clients.md)
- [remaining-asset-value.md](remaining-asset-value.md)
- [books.md](books.md)
- [../accounting/scenario-lines.md](../accounting/scenario-lines.md)

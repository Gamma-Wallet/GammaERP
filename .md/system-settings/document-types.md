# Document types

The configuration of each kind of document: whether it posts, its reversal, who may use it, its sides, its lines, its VAT groups and its payment methods. Everything on this screen is the company's configuration rows.

**Where:** System settings › Document types. **Who:** anyone with it in their menu reads. Only an administrator changes anything: the fields stay off for others.

On the left are the types by document category. A type sits in the first category it is in. Those in none show under Other. Types under another type (journal types of a sheet) do not show.

Choose a type, then a tab:

| Tab | Page |
|---|---|
| General, Users | This page |
| From / To | [Document type sides](document-type-sides.md) |
| Transaction types, VAT groups, Payment methods | [Document type transactions](document-type-transactions.md) |

## Actions

### Add a document type or category

The screen has no New button. A new type starts in a file and is finished here.

Steps:

1. On top, under Document categories or Document types, press **Export** or **Template**.
2. Fill in Code, Name, Parent, Category, First number, Last number, Posting books nothing, Partial reversal, Reversal, Reverses, Reverses parent.
3. Press **Import**, then **Confirm**. See [Migration](migration.md).
4. Choose the new type and fill in its tabs.

Result: the type exists. The buttons show for those with Migration in their menu.

API: `POST /migration/DOCUMENT_TYPES/import`, `POST /migration/DOCUMENT_CATEGORIES/import`. Also `GET /documents/types`, `POST /documents/types`, `GET /documents/categories`, `POST /documents/categories`. The two saves need the Document types screen in your menu, checked on the server (`NotAllowedError: this screen is not in your menu.`); administrators pass. Reading stays open.

### Set the General tab

Steps:

1. Choose the type. Stay on **General**.
2. Tick what applies:

| Tick | Column | Meaning |
|---|---|---|
| Posting books nothing (offers, contracts, orders) | `NeverPosted` | Posting writes no entry, moves no stock and opens no invoice; it only makes the document final. An invoice (a type that holds an open item) is made from it only once posted. A point of sale's order type is not posted at all. |
| A reversal may take part of the lines | `AllowsPartialReversal` | A reversal may take some lines, or less than their quantity. |
| Its lines go to the production point display | `SendsToProduction` | With **Production document made** (`ProductionDocumentTypeId`): the document made when a production point says a line was produced. |
| A line from a produced order takes the product itself | `SellsProducedProduct` | The line takes the product, not its recipe's ingredients. |
| Its orders are listed on the Production screen, to be produced | `ProducedOnProductionScreen` | The type is offered under **Order type** on Production → Orders to produce. The starter packs tick it for Sales order. |
| A new repair job opens this type | `StartsRepair` | Used by the repair board. |
| Can be marked accepted or refused by the client | `CanBeAccepted` | A person can mark its documents. |
| This type is a reversal | `IsReversal` | With **It reverses** (`ReversedDocumentTypeId`): the one type it reverses. |
| An invoice: it stays open until a payment covers it | `HoldsOpenItem` | Its posted documents are open items until a payment covers them. See [Payments and open invoices](../documents/payments-and-open-invoices.md). |
| A payment: its lines name the invoices they cover | `PaysOpenItems` | Its lines name the open documents they cover, by hand. |
| A contract billed by a plan | `BillsByPlan` | Its documents carry a billing plan. See [Billing plan](../contracts/billing-plan.md). |
| Over the limit of its writer it waits for approval | `NeedsApproval` | A document over its writer's approval limit waits. See [Approvals](../documents/approvals.md). |
| Its posted documents sell the fixed assets on their lines | `SellsAssets` | Posting marks the fixed assets on its lines sold. See [Selling an asset](../fixed-assets/selling-an-asset.md). |

3. Press **Save**. All the boxes are saved together.

Result: `tbl_document_type` changes, and the old row stays as a history copy.

The lists of types on this tab (**It reverses**, **Production document made**) offer every type but the one open, types under another type (journal types of a sheet) too.

A reversal type's documents are made only by reversing a posted document of the type it reverses. A type no reversal type reverses can't be reversed until one is set.

Refused when:

- `NotAllowedError: only an administrator changes the configuration.`
- "There is no such document type."
- "The type it reverses is not a type of the same company."
- `NotConfiguredError: the database refuses it (ux_document_type_reversed).` Two active reversal types can't reverse the same type.
- `NotConfiguredError: the database refuses it (ck_document_type_reversal).` A reversal type must name its type, and any other type must name none.

API: `GET /documents/types/{id}/settings`, `PUT /documents/types/{id}/settings`

### Set the Users tab

Only the users and roles chosen here see the type in their menu and lists, and may write its documents.

Steps:

1. Open **Users**.
2. Choose Users and Roles.
3. Press **Save**.

Result: the type is given to exactly these. A new type shows in their menu with no menu change, under its category's item. An administrator always has every type.

Refused when: "Only an administrator gives document types to users." (403), or "The document type does not exist."

API: `GET /documents/types/{id}/users`, `PUT /documents/types/{id}/users` with `userIds` and `roleIds`. `GET /documents/types/allowed?category=` gives the signed-in user's types.

### Numbering

A type's codes come from its First number and Last number, in the file above. The next number is changed in the Migration step Next document numbers (`NUMBERS`).

## Related

- [Document type sides](document-type-sides.md)
- [Document type transactions](document-type-transactions.md)
- [Document flows](document-flows.md)
- [Migration](migration.md)
- [Reversing](../documents/reversing.md)

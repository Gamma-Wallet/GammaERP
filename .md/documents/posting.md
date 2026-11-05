# Posting a document

Looking at the entries a document would write, and writing them. Posting is final.

**Where:** a document › Posting tab, and the **Post** button. **Who:** write on the document's category. On an offer, a contract or an order, posting books nothing: see below.

## Actions

### See the posting preview

Steps:
1. Save the document.
2. Open the **Posting** tab.

Result: a table of Line, Account, Name, Cost centre, Debit, Credit, with totals. Nothing is written. A sheet shows its journals' entries. The preview reads the saved document, so unsaved changes do not show.

For a posted document the tab shows "The entries this document wrote": the entries posting wrote (`charts.tbl_chart_value`), with those of the journals under a sheet. They are read as written, not worked out again.

Refused when: the same errors as posting. The message shows on the tab in place of the table; the tab stops showing "Loading…". Serial numbers and lots are not checked here.

API: `GET documents/{id}/posting-preview`.

### Post

Steps:
1. Press **Post**. An unsaved document is saved first.
2. Confirm: "A posted document is final: its entries are written to the books. Mistakes are corrected by reversing it."

Result: everything below happens together, or nothing does.

- **Accounts:** the entries are written, with their signed copies. Each line takes the most specific scenario of its document type × transaction type. Entries are repeated on every other chart the company maps to (corporate, IFRS…), carrying its country.
- **Balances:** the from and to sides' balances move, by the amounts their side rows name. A line's own element moves too, where the type × line type says so (journal rows on a client or staff member).
- **Stock:** quantities move between holders. Recipes give up ingredients.
- **Cost:** cost layers are taken, made or carried, and each line keeps its cost.
- **Product value and purchase price:** the product's value moves, and a purchase records its price.
- **Serial numbers and lots** move to their new holder.
- **Due date:** set on an open-item type if empty.
- **Payments:** a line naming an invoice is checked and linked to it.
- **Fixed assets:** on a type that sells assets, an asset on a line is closed at its actual value and marked sold in every depreciation book.
- **Group:** a sale that a Purchases from the group setting names makes an unposted purchase in the buying company, in the same transaction. If that is refused, the posting is refused.
- All holds on the document end.

A sheet posts with its journals. See [Journal](categories/journal.md).

Refused when, in this order:

| Message | Meaning |
|---------|---------|
| The document does not exist, or you may not write it. | |
| `PostedError`, `LockedError` | Already posted, or held by another user |
| A document is posted on its posting date: it has none. | Fill in the posting date |
| `PeriodClosedError`: the period is closed up to {date}. | Reversals are refused too |
| Any line refusal | See [lines.md](lines.md#refused-on-a-line). A purchase made from a group's sale may still have open lines |
| A foreign currency needs its exchange rate: set it on the document and save it. | |
| `NotAllowedError`: it waits for approval. | The type needs approval and the total is over its writer's limit. See [approvals](approvals.md) |
| Open a shift first: this point of sale has none open. | The type is a point of sale's bill or order and that point requires a shift |
| Open a shift first: the point of sale of this cash desk has none open. | The type is a point of sale's money in or out, a line names its cash desk, and that point requires a shift |
| `StockError` | See [lines.md](lines.md#stock-and-cost-checks) |
| `OpenItemError` | See [payments-and-open-invoices.md](payments-and-open-invoices.md) |
| `NoScenarioError`: no scenario fits the transaction {n} ({item}). | Add one under Accounting setup › Posting scenarios |
| `NotConfiguredError`: the transaction's scenario has no lines. | |
| `NotConfiguredError`: a line of the scenario has no account or no value that names an amount of the transaction. | |
| `GroupAccountError`: {code} has accounts under it; post to one of those. | |
| `UnbalancedError`: debit x ≠ credit y. | |
| `MappingError`: in chart {c}, {code} has no account / has more than one. | Fix it in Accounting setup › [Account mapping](../accounting/account-mapping.md) |
| The sheet has no rows to post. | |

Line errors, stock errors and scenario errors each come as a full list, not one at a time.

API: `POST documents/{id}/post`.

### Post an offer, a contract or an order

Types marked *Posting books nothing (offers, contracts, orders)* on System settings › Document types (`NeverPosted`).

Steps:
1. Open the saved document and press **Post**.

Result: the document and its lines are marked posted. No entry is written, no stock moves, no invoice opens. A missing posting date takes the document date. From then on the document is final: it can't be changed, and its print loses the "not posted" mark. Invoices are made from it only now. It can still be switched off while nothing active was made from it.

Refused when:
- the checks every posting makes: already posted, held by another user, no posting date, `PeriodClosedError`, an approval limit.
- "A point-of-sale order is billed at the till, not posted.": the type is a point of sale's order type.

API: `POST /documents/{id}/post`

## Related

- [Reversing](reversing.md)
- [Lines](lines.md)
- [Payments and open invoices](payments-and-open-invoices.md)

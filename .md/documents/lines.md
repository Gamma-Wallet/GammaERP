# Lines of a document

The grid of a document: what each line holds, the price it proposes, serial numbers and lots, and the stock and cost checks.

**Where:** a document › Lines tab. **Who:** as for saving the document. A sheet has rows instead of lines: see [Journal](categories/journal.md).

The grid shows a column only when one of the type's kinds of line uses it. A cell is off where its kind does not need it.

## Actions

### Add a line

Steps:
1. Press **Add line**. With several kinds of line, pick the kind: Item, Service item, Account and so on.
2. Or press Enter in a cell of the last line: a line of the first kind is added.

Result: a new line with the kind's default quantity and discount (else quantity 1), the first VAT group, and the document's currency.

API: lines are saved with the document in `transactions`.

### Pick the item

Steps:
1. Search and pick in **Item / account**.

Result: for a product the line proposes its units, a price, its VAT group, and asks for lots or serial numbers if the product tracks them. The person may change any of them.

| Proposed | From |
|----------|------|
| Units | The product's own, else its type's, the first as default |
| Price, partner on the To side (a sale) | The most particular sale price: partner type, partner category, unit, currency, then the latest. With none for the product, its type's price for the unit (Sale prices by partner), the closest match to the partner first. See [Sale prices](../products/sale-prices.md#how-a-line-proposes-a-price). On a type that sells assets (`sells_assets`), a fixed asset proposes what is left of its value |
| Price, partner on the From side (a purchase) | The line price of the latest purchase of that product in that unit |
| VAT group | The product's, else its product type's, else that of the last purchase, if the document type offers it |

Changing the unit asks for the proposal again.

Refused when: a product of another kind is put on a line, for example goods on a services line. `NotConfiguredError: {item} is not one of the line's {scope}.` The whole document is refused.

API: `GET documents/line-proposal?documentType=&product=&partner=&unit=&currency=`.

### Type the amounts

Steps:
1. Type **Quantity**, **Price** and **Discount** (per unit), and pick **VAT**.
2. On types that pay open items, type the **Invoice number**: see [payments-and-open-invoices.md](payments-and-open-invoices.md).

Result: on each line:
- Net = (price − discount) × quantity
- VAT amount = net × the VAT group's rate / 100
- Total = net + VAT amount
- Each Lcu amount = the amount × the exchange rate

Totals below the grid show Discount, Net, VAT and Total.

### Choose a line's place

The **Place** column shows when a side of the document is a cost centre: the From side when the document takes stock out of one, else the To side. It lets one line use another place than the document's.

Steps:
1. In **Place**, search and pick a cost centre. The choices are those the document's side offers.
2. Leave it empty to use the document's place: the box then shows "The document's place".

Result: the line's own From (or To) is that place, and the stock and cost checks read it for that line. Changing the From place clears the lots chosen on the line. On a read-only document the column is read only.

Refused when: as for the document's own side, at save and post.

API: `transactions[].fromId` or `transactions[].toId`. Empty takes the document's side.

### Change the kind or remove a line

Steps:
1. Pick another **Kind**: the item and unit are cleared.
2. Or press the bin at the end of the line.

Result: the change is stored on Save.

### Give serial numbers

For a product whose product type tracks serial numbers. The line shows "Serial numbers: 2 of 3".

Steps:
1. Click that text.
2. When the from side is a partner or empty (a purchase), type one number per line. A part number may follow a comma, a semicolon or a tab.
3. Otherwise tick the numbers the from side holds. Search helps.
4. Press **Done**.

Result: the numbers stay on the draft. A draft may be saved without them. On a posted document the dialog is read only.

Refused when (at posting, all `StockError`):
- {item} tracks serial numbers: x are needed, y are given. Give them on an invoice's line; a point of sale can't take them.
- Serial number x is given twice.
- Serial number x of {item} is not here. (A place gives up only what it holds.)
- Serial number x of {item} already exists. (It came new but is known.)
- Serial number x of {item} is held elsewhere.

Posting moves each number to its new holder. See the Serial numbers screen under Stock and production.

API: `transactions[].serials` as `{ serial, partNumber }`. `GET serials?product=&holder=` lists what a holder has.

### Name a lot

For a product whose product type tracks lots. Two boxes show under the item: lot and expiry date.

Steps:
1. Type the lot, or click into the box to see the lots the from side holds, with their quantity and expiry. A from side that is a partner offers none: a purchase types a new lot.
2. Type the expiry date for a new lot.

Result: posting moves the quantity of that lot from one holder to the other. A lot is made the first time it is met.

Refused when (at posting, all `StockError`):
- {item} tracks lots: give its lot on the line.
- Lot x of {item} expires on {date}. (The typed expiry differs from the lot's.)
- Lot x of {item} expired on {date}. Only on a sale to a partner of a product type that refuses expired lots; a reversal passes.
- There are x of lot y of {item} here, z are needed. A partner on the from side is never refused.
- {item} tracks lots and can't go through production or a stock conversion.
- {item} tracks lots and can't be used in a recipe.

API: `transactions[].lot`, `lotExpiryDate`. `GET lots/held?product=&holder=&unit=`.

## Stock and cost checks

**When:** the stock and cost checks run on every **save** and again at **post**. They do not run on types whose posting books nothing (offers, orders, contracts, repair documents): those move nothing. Serial numbers and lots are checked at post only. The checks read the lines whose transaction configuration moves stock (`stock_direction`): 1 from one side to the other, 2 used, 3 produced, 4 waste, 5 value only (disassembly waste: no stock moves), 0 none.

| Refused (`StockError`) | Meaning |
|------------------------|---------|
| there are x of {item}, y are needed; take them from {places} / no other place has them. | The company forbids negative stock for that kind of holder, and this place would go below zero. Pick one of the places named in the line's **Place** column, or split the line |
| the company holds x of {item}, y are needed. | More than all the company's places hold together. This holds even where negative stock is allowed, for a side that takes cost layers |
| {item} comes in only with a purchase bill or from another place of the company. | Stock comes in only with a cost |

**Negative stock** is a company setting per kind of holder, the company properties `Negative stock allowed (cost centres)`, `(partners)`, `(employees)`: 0 forbids, 1 or empty allows. Where a place may go below zero, the part it lacks is valued at the oldest cost the company holds elsewhere. What comes in next settles it, and the difference goes into the product's value.

**Cost** is first in, first out, per place. A purchase makes a cost layer; a sale or a transfer takes the oldest layers; a transfer carries them with their cost. The line keeps the cost it moved (`cost_lcu`). Which side does what is the side row's `cost_layers`.

**Recipes:** a composite product with a recipe gives up its ingredients instead of itself, each the line's quantity × its quantity, waste included. A line that comes from a produced order line takes the product itself, where the type says so.

### Refused on a line

These show under the line, on save and again at post:

| Message | Meaning |
|---------|---------|
| `FillError`: line n needs its {field}, chosen from the list. | The field is required for this document type × transaction type (`tbl_transaction_fill`: 0 typed or chosen, 1 chosen, 2 not needed) |
| `NotConfiguredError`: the document type does not have this transaction type. | |
| The transaction type has several configurations: choose one. | |
| `NotConfiguredError`: the VAT group is not one of the document type's. | |
| The currency does not exist. | |
| A foreign currency needs its exchange rate. | No rate typed, and none on or before the document's day |

## Related

- [Drafts](drafts.md)
- [Posting](posting.md)
- [Production documents](categories/production.md)

# Tables (Restaurant / café)

The floor plan of a bar or restaurant: halls, tables, open amounts and waiters. A waiter sends orders to a table, and one bill is made at payment.

**Where:** Point of sale › Open the point of sale, at a Restaurant / café. **Who:** the screen `pos`. Arranging needs an administrator.

## Order or direct bill

The setting `OrderTypeId` decides what Send does. The base pack sets it on the Restaurant / café row. One point of sale can set its own on its tab, field "Order sent first, bill at payment". See [settings.md](settings.md).

| `OrderTypeId` | Send writes | Stock moves |
|---------------|-------------|-------------|
| Set (pack) | An order, saved and not posted | When the table is paid and the bill posts |
| Empty | A posted bill | At once |

The rest of this page assumes the pack's order flow.

## Actions

### Read the floor plan

Each hall is a tab. Tables without a hall come under *Other places*, or *Tables* when there are no halls. Counters show like tables.

A table shows its name and, when something is open, the open amount and who wrote it. A free table shows *Free* and its seats. *Open* at the top is the total of all places. *My tables* shows only tables where you wrote something open.

A table's place is its Position X and Position Y properties. A table with no position takes the next cell of a grid, six to a row.

**API:** `GET /pos/places?pointOfSale=`

### Arrange the plan

**Who:** an administrator. The Arrange button is hidden for others.

**Steps**
1. Press Arrange.
2. Drag the tables.
3. Press Save the plan, or Cancel to drop the changes.

**Result:** each moved table's Position X and Position Y are saved on its cost centre. Tables cannot be opened while arranging.

**Refused when:** a place's type has no Position X / Y properties ("{place} has no Position X / Y properties."). The pack's Table type has them; Counter does not, so moving a counter stops the save with this message.

**API:** `POST /cost-centers` for each moved table.

### Take and send an order

**Steps**
1. Tap a table.
2. Tap a category, then products. Each tap adds one. Prices and totals on screen use each product's own VAT rate, from its line proposal, learned when the product is first added to the order. Until then its tile shows the price at the bill type's first VAT rate. Use the plus and minus on a line; at zero the line goes. A product with no price cannot be tapped.
3. For a product that tracks lots, choose the lot: see [lot-picker.md](lot-picker.md).
4. Press Send.

**Result:** one order document at the table, written to the walk-in client, with one line per product leaving the point of sale at the menu's price. It is saved, not posted: no stock, no balances, no bill. A message says "Order {code} sent for {table}" and the floor plan comes back. Lines routed to a production point appear on its [display](production-point/display.md). Every Send makes a new order, so a table can hold several.

**Refused when**
- "{product} fits none of the lines of {type}: check its category in the products register.": the product's category does not fit a line of the document type.
- "{product} has no unit to sell in: give it one in the products register."
- "this document type has not been given to you or to one of your roles."
- "Open a shift first: this point of sale has none open.": the point of sale requires a shift. The server checks it when the order is saved, and when a direct bill is posted. See [shifts.md](shifts.md).

**API:** `GET /documents/line-proposal`, `POST /documents`. Without an order type, also `POST /documents/{id}/post`.

### See what is open at a table

The right side of the order screen shows each open order with its code, amount and a Cancel button, and the number and total of open bills. The Pay button shows the total of both.

**API:** `GET /pos/bills/unpaid?type=&table=`, `GET /pos/orders/open?type=&table=`

### Cancel an order

**Steps:** press Cancel beside the order at the table.

**Result:** the order is switched off and leaves the table and the display. **What was already produced stays in stock.** A production document is not undone, so the product stays at the point of sale. See [production-point/display.md](production-point/display.md).

**Refused when:**
- `NotAllowedError`: this order is billed; reverse the bill instead.
- The period is closed, or you may not write the document.

**API:** `DELETE /pos/orders/{id}`

### Pay the table

**Steps**
1. Open the table and press Pay, which shows the amount.
2. The open orders become one bill. Then the [pay screen](paying.md) opens with the table's bills.

**Result:** one posted bill from all the table's open orders, each line pointing at its order line, dated today. Stock leaves the point of sale now:
- A line that was produced takes the product itself, because the type sets `sells_produced_product`.
- A line not produced takes the product, or a composite product's ingredients by its recipe.

The orders are linked to the bill and stop being open.

**Refused when**
- "not produced yet: {names}.": a production point still has to produce something routed to it. The bill waits.
- "this order does not make documents of that type.": no row in System settings › Document flows from the order type to the bill type.
- "There is no open order to bill."
- "Open a shift first: this point of sale has none open."

**API:** `POST /pos/orders/bill` with `billTypeId`, `orderTypeId`, `tableId` and optional `orderIds`.

The product tiles of a category show their prices at each product's own VAT rate: the screen learns the rates of the tiles on screen as soon as the category opens.

## Related

- [paying.md](paying.md)
- [production-point/display.md](production-point/display.md)
- [lot-picker.md](lot-picker.md)
- [settings.md](settings.md)


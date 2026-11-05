# Production point display

What a kitchen or bar has to make, oldest first. A person says each item was produced. With a recipe that makes a production document.

**Where:** Point of sale › Production point display. **Who:** the screen `production-display`. The base pack gives it to the role Production point.

## How an order arrives

A waiter sends an order at a table. Each product line of an order whose document type sets `sends_to_production` goes to the production point its [route](routes.md) names. An order stays on the display, however old, until it is billed or cancelled, so an order still open can always be said produced and its table's bill does not get stuck.

## Actions

### Choose a production point

**Steps**
1. Choose the production point. With only one, it is chosen for you.
2. Press Full screen if the screen hangs in the kitchen.

**Result:** the board shows one card per order. Each card has the table (or the order's code when there is no table), the minutes since it was sent, the order's code, the waiter, and the lines still to make. The open screen reads the board again every 15 seconds; this only reads.

A card turns late after the lowest *Warning minutes* of the point's routes, or 15 minutes when no route sets one.

If there are no points, the screen says "Add a cost centre of type Production point first."

**API:** `GET /production-display/points`, `GET /production-display/{point}`

### Say an item was produced

**Steps**
1. Press Produced on a line, or All produced on a card.

**Result:** all the lines or none, per press:
- **A product with a recipe:** a posted production document of the type named by the order's document type. The recipe's ingredients leave the production point, the product comes into the point of sale the order line names, and the books move through the production account. That point of sale may be one the cook does not see: Produced delivers anywhere in the company. Every other stock document still needs both places to be ones the user sees. The bill later takes the product itself.
- **An item with no recipe:** only marked as produced. No document, no stock. The bill later takes it as usual.

The line moves to the Produced list with the document's code. A line already produced is skipped quietly.

**Refused when**
- "this order's document type names no production document.": set `production_document_type_id`.
- "this document type has not been given to you or to one of your roles.": give the production document type to the role. The pack gives Production to the role Production point. See [settings.md](settings.md).
- "This line is not open at this production point.": it was billed, cancelled or routed elsewhere.
- "the product has no recipe for this unit; give the ingredients used.": the recipe is for another unit.
- Stock refusals from posting, when ingredients are short.
- "This line has no point of sale."
- "Production points belong to a company: choose one of its nodes."

**API:** `POST /production-display/{point}/produced` with `lineIds`.

### Take an item back

**Steps:** in the Produced list, press Undo. It shows only on lines with no production document.

**Result:** the line goes back on its card.

**Refused when:** "This product was produced and can't be undone."

**API:** `POST /production-display/{point}/undo` with `lineIds`.

### What can't be undone

Once a recipe has made a production document:
- The display has no Undo for it.
- Cancelling the order leaves the product in stock at the point of sale. The ingredients stay used.
- A bill for the table is still possible, because the line counts as produced.

### See what was produced

The Produced list shows the last 20 lines of orders still open, with quantity, name, table or code, and production document.

### See what is not routed

*Ordered with no production point* lists the product names ordered in the last seven days on open orders with no route, so nothing is lost. It is read when the screen opens and is shown under a chosen point. These lines appear on no display and do not hold up a bill.

To fix one, add a [route](routes.md).

**API:** `GET /production-display/not-routed`

## A bill waits for production

Paying a table is refused with "not produced yet: {names}" while anything routed is not produced. See [../tables.md](../tables.md). Items with no route do not wait.

## Related

- [routes.md](routes.md)
- [settings.md](settings.md)
- [../tables.md](../tables.md)


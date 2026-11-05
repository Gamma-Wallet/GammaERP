# Purchase price history

What a product was bought at in a period, oldest first.

**Where:** Reports › Stock and sales › Purchase price history. **Who:** the `purchase-prices` report in your menu.

## Actions

### Show a product's purchase prices

Filters: **Product** (required), **From**, **Up to**.

Steps:
1. Choose the product, set the period and press **Show**.

Shows:
- Figures: Last price, Lowest, Highest. Reversed purchases do not count.
- A line chart of the unit price, with two prices or more.
- Table: Date, Document, Supplier, Quantity, Unit, Unit price, Value, Reversed by.

It lists the lines of posted documents that recorded a purchase price. A reversed purchase stays, with its reversal beside it. The unit price is the line's value over its quantity, in the company's currency.

Result: nothing changes. Clicking a row opens the document.

API: `GET /reports/purchase-prices?product=&from=&to=`.

## Related

- [item-movements.md](item-movements.md)

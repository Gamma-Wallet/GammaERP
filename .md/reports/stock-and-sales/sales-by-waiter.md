# Sales by waiter

The totals of the chosen documents per user who wrote them.

**Where:** Reports › Stock and sales › Sales by waiter. **Who:** the `lines-by-user` report in your menu.

## Actions

### Show the totals by waiter

Filters: **Document categories** (required, one or more), **From**, **Up to**.

Steps:
1. Choose the categories, set the period and press **Show**.

Shows:
- Figures: Net, VAT, Total.
- A bar chart of the top eight.
- Table: Waiter, Quantity, Net, VAT, Total, the largest first.

The waiter is the user who created the document. Reversals count negative.

Result: nothing changes. A row opens nothing.

Refused when: "Name at least one document category."

API: `GET /reports/lines?category=&from=&to=`, read from `byUser`.

## Related

- [sales-and-purchases-by-line.md](sales-and-purchases-by-line.md)

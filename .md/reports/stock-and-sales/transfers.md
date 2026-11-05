# Transfers

The lines of transfer documents in a period, from where to where, at their stock cost.

**Where:** Reports › Stock and sales › Transfers. **Who:** the `transfers` report in your menu.

## Actions

### Show the transfers

Filters: **Document categories** (required, one or more), **From**, **Up to**, **Cost centre**.

Steps:
1. Choose the Transfers category. The report does not choose it for you.
2. Set the other filters and press **Show**.

Shows:
- Figures: Lines, Value.
- Table: Date, Document, From, To, Item, Quantity, Unit, Value.

Value is the line's stock cost. Reversals count negative.

Result: nothing changes. Clicking a row opens the document.

Refused when: "Name at least one document category."

API: `GET /reports/lines?category=&from=&to=&costCenter=`.

## Related

- [sales-and-purchases-by-line.md](sales-and-purchases-by-line.md)
- [../../fixed-assets/assets-at-clients.md](../../fixed-assets/assets-at-clients.md)

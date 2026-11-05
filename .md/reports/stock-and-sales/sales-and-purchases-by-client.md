# Sales and purchases by client

The totals of the chosen documents per client or supplier. Also called lines by partner.

**Where:** Reports › Stock and sales › Sales and purchases by client. **Who:** the `lines-by-partner` report in your menu.

## Actions

### Show the totals by client

Filters: **Document categories** (required, one or more), **From**, **Up to**.

Steps:
1. Choose the categories, set the period and press **Show**.

Shows:
- Figures: Net, VAT, Total.
- A bar chart of the top eight.
- Table: Client or supplier, Quantity, Net, VAT, Total, the largest first. Lines with no partner show as —.

Reversals count negative.

Result: nothing changes. Clicking a row opens Sales and purchases by line for that client or supplier.

Refused when: "Name at least one document category."

API: `GET /reports/lines?category=&from=&to=`, read from `byPartner`.

## Related

- [sales-and-purchases-by-line.md](sales-and-purchases-by-line.md)
- [client-statistics.md](client-statistics.md)

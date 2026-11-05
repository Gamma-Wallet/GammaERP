# Sales and purchases by cost centre

The totals of the chosen documents per cost centre.

**Where:** Reports › Stock and sales › Sales and purchases by cost centre. **Who:** the `lines-by-cost-center` report in your menu.

## Actions

### Show the totals by cost centre

Filters: **Document categories** (required, one or more), **From**, **Up to**.

Steps:
1. Choose the categories, set the period and press **Show**.

Shows:
- Figures: Net, VAT, Total.
- A bar chart of the top eight.
- Table: Cost centre, Quantity, Net, VAT, Total, the largest first.

A line's cost centre is the first that is one of: the line's from side, the document's from side, the document's to side. Reversals count negative.

Result: nothing changes. Clicking a row opens Sales and purchases by line for that cost centre.

Refused when: "Name at least one document category."

API: `GET /reports/lines?category=&from=&to=`, read from `byCostCenter`.

## Related

- [sales-and-purchases-by-line.md](sales-and-purchases-by-line.md)
- [../financial/cost-centre-ledger.md](../financial/cost-centre-ledger.md)

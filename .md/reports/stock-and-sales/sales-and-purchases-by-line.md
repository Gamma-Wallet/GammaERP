# Sales and purchases by line

The lines of the posted documents of chosen categories in a period. The category you choose decides whether this is sales, purchases or something else.

**Where:** Reports › Stock and sales › Sales and purchases by line. **Who:** the `lines` report in your menu.

## Actions

### Show the lines

Filters: **Document categories** (required, one or more), **From**, **Up to**, **Cost centre**, **Partner**.

Steps:
1. Choose the categories, set the other filters and press **Show**.

Shows:
- Figures: Net, VAT, Total.
- A bar chart of the top eight partners by total.
- Table: Date, Document, Partner, Cost centre, Item, Quantity, Net, VAT, Total.

The report holds those categories' document types and the reversal types of them, which count negative.

Result: nothing changes. Clicking a row opens the document.

Refused when: "Name at least one document category."

API: `GET /reports/lines?category=&category=&from=&to=&costCenter=&partner=`. Excel: `/reports/lines/xlsx`. PDF: `/reports/lines/pdf`, same parameters. Any of the rights `lines`, `lines-by-partner`, `lines-by-cost-center`, `lines-by-user`, `transfers` opens these calls.

The answer also holds the totals by cost centre, partner and user: `byCostCenter`, `byPartner`, `byUser`.

## Related

- [sales-and-purchases-by-client.md](sales-and-purchases-by-client.md)
- [sales-and-purchases-by-cost-centre.md](sales-and-purchases-by-cost-centre.md)
- [sales-by-waiter.md](sales-by-waiter.md)
- [transfers.md](transfers.md)

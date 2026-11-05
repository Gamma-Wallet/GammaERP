# Document totals

The totals per document type of the chosen categories in a period.

**Where:** Reports › Stock and sales › Document totals. **Who:** the `document-totals` report in your menu.

## Actions

### Show the totals

Filters: **Document categories** (required, one or more), **From**, **Up to**.

Steps:
1. Choose the categories, set the period and press **Show**.

Shows:
- Figures: Documents, Net, VAT, Total.
- Table: Document type, Documents, Net, VAT, Total.

Posted documents only. The reversal types of those categories' types are included and count negative.

Result: nothing changes. A row opens nothing.

Refused when:
- "Name at least one document category."
- "The period ends before it starts."

API: `GET /reports/document-totals?category=&category=&from=&to=`.

## Related

- [sales-and-purchases-by-line.md](sales-and-purchases-by-line.md)
- [../dashboard.md](../dashboard.md)

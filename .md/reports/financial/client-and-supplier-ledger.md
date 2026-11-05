# Client and supplier ledger

One client's or supplier's documents in a period, with a running balance. Also called the partner ledger.

**Where:** Reports › Financial › Client and supplier ledger. **Who:** the `partner-ledger` report in your menu.

## Actions

### Show a partner's ledger

Filters: **Partner** (required), **From**, **Up to**.

Steps:
1. Choose the partner, set the period and press **Show**.

Shows:
- Figures: Debit, Credit, Closing balance.
- A line chart of the balance over the period.
- Table: Date, Document, Category, Debit, Credit, Balance. The first row is the balance before the period. Each further row is one posted document the request sees.

Result: nothing changes. Clicking a document's row opens it. Amounts come from `tbl_partner_balance`.

API: `GET /partners/{id}/ledger?from=&to=`. Excel: `/partners/{id}/ledger/xlsx`. PDF: `/partners/{id}/ledger/pdf`.

## Related

- [open-invoices.md](open-invoices.md)
- [../stock-and-sales/client-statistics.md](../stock-and-sales/client-statistics.md)

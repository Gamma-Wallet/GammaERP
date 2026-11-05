# Cost centre ledger

The posted documents behind a cost centre's balance in a period, with a running balance.

**Where:** Reports › Financial › Cost centre ledger. **Who:** the `cost-center-ledger` report in your menu.

## Actions

### Show a cost centre's ledger

Filters: **Cost centre** (required), **From**, **Up to**.

Steps:
1. Choose the cost centre, set the period and press **Show**.

Shows:
- Figures: Debit, Credit, Closing balance.
- A line chart of the balance over the period.
- Table: Date, Document, Debit, Credit, Balance. The first row is the balance before the period.

Result: nothing changes. Clicking a document's row opens it. Amounts come from `tbl_cost_center_balance`.

Refused when:
- "The period ends before it starts."
- Not found: the request does not see the cost centre.

API: `GET /cost-centers/{id}/ledger?from=&to=`. Excel: `/cost-centers/{id}/ledger/xlsx`. PDF: `/cost-centers/{id}/ledger/pdf`. The balance alone, per currency: `GET /cost-centers/{id}/balance?until=`.

## Related

- [staff-ledger.md](staff-ledger.md)
- [account-ledger.md](account-ledger.md)

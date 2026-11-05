# Staff ledger

The documents behind a member of staff's balance in a period, with a running balance.

**Where:** Reports › Financial › Staff ledger. **Who:** the `staff-ledger` report in your menu.

## Actions

### Show a member of staff's ledger

Filters: **Staff** (required), **From**, **Up to**.

Steps:
1. Choose the person, set the period and press **Show**.

Shows:
- Figures: Debit, Credit, Closing balance.
- A line chart of the balance over the period.
- Table: Date, Document, Debit, Credit, Balance. The first row is the balance before the period.

Result: nothing changes. Clicking a document's row opens it. Amounts come from `tbl_employee_balance`.

Refused when:
- "The period ends before it starts."
- Not found: the request does not see this person.

API: `GET /employees/{id}/ledger?from=&to=`.

## Related

- [cost-centre-ledger.md](cost-centre-ledger.md)
- [../README.md](../README.md)

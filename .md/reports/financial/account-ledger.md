# Account ledger

The entries of an account, and of every account under it, in a period, with a running balance.

**Where:** Reports › Financial › Account ledger. **Who:** the `ledger` report in your menu.

## Actions

### Show an account's ledger

Filters: **Account** (required), **From**, **Up to**, **Cost centre** (empty: all).

Steps:
1. Choose the account, set the period and press **Show**.

Shows:
- Figures: Opening balance, Debit, Credit, Closing balance.
- A line chart of the balance over the period, with two dated rows or more.
- Table: Date, Document, Account, From, To, Debit, Credit, Balance.

Result: nothing changes. Clicking a row opens its document.

Refused when:
- "The period ends before it starts."
- Not found: the account does not exist.

API: `GET /reports/ledger?account=&from=&to=&costCenter=`. Excel: `/reports/ledger/xlsx`. PDF: `/reports/ledger/pdf`, same parameters.

## Related

- [trial-balance.md](trial-balance.md)
- [../README.md](../README.md)

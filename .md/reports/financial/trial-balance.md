# Trial balance

Per account: the balance before the period, the period's debit and credit, the balance at its end, and the year to date. Each level of the chart sums all the accounts below it.

**Where:** Reports › Financial › Trial balance. **Who:** the `trial-balance` report in your menu.

## Actions

### Show the trial balance

Filters: **From**, **Up to**, **Chart of accounts** (empty: the company's statutory chart), **Cost centre** (empty: all).

Steps:
1. Set the filters and press **Show**.

Shows:
- Figures: Total debit, Total credit, Difference (Balanced or Not balanced). Totals come from the accounts with nothing under them.
- Table: Account, Name, Opening debit and credit, Debit, Credit, Closing debit and credit, and Debit and Credit, year to date. Accounts with nothing on them are left out; the groups above those kept stay.

Year to date runs from the start of the fiscal year the period's end falls in.

Result: nothing changes. Clicking a row opens its Account ledger, with the same period and cost centre.

Refused when:
- "The period ends before it starts."
- "There is no such chart."
- "Name the chart: the request is not inside one company."
- "The company has no statutory chart."

API: `GET /reports/trial-balance?from=&to=&chart=&costCenter=`. Excel: `/reports/trial-balance/xlsx`. PDF: `/reports/trial-balance/pdf`, same parameters.

## Related

- [account-ledger.md](account-ledger.md)
- [../../accounting/chart-of-accounts.md](../../accounting/chart-of-accounts.md)
- [../../accounting/year-closing.md](../../accounting/year-closing.md)

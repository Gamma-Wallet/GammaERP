# Balance sheet

The company's position at a day, beside the same day a year before. The lines and the accounts they hold are configuration.

**Where:** Reports › Financial › Balance sheet. **Who:** the `balance-sheet` report in your menu.

## Actions

### Show the balance sheet

Filters: **At**, a day. It opens at today.

Steps:
1. Set **At**, choose the **Chart** (empty: the statutory one) and press **Show**.

Shows:
- Figures: Assets; Liabilities and equity; Difference, which says whether the two sides meet; Accounts on no line, when there are any, with their codes.
- Table: Line, Name, Amount, A year before. Each level sits further in, and a heading shows the total below it.

Result: nothing changes. Balances are the posted entries up to the day. One chart (tree) at a time: the one chosen under **Chart**, else the company's statutory chart category (as the trial balance). Posting groups can post one entry to several trees through the account mapping, so trees are never added together. When no category is marked statutory and none is chosen, every chart is read. The result lines are worked out from what the profit and loss accounts hold: result kind 1 from the start of the fiscal year, result kind 2 before it. No closing entry moves them.

Refused when: `NotAllowedError` without the report in your menu.

API: `GET /reports/balance-sheet?to=&chart=`.

## Related

- [profit-and-loss.md](profit-and-loss.md)
- [../../accounting/statement-lines.md](../../accounting/statement-lines.md)
- [../README.md](../README.md)

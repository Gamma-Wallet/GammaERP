# Profit and loss

The result of a period, beside the same period a year before. The lines and the accounts they hold are configuration.

**Where:** Reports › Financial › Profit and loss. **Who:** the `profit-loss` report in your menu.

## Actions

### Show profit and loss

Filters: **From**, **Up to**.

Steps:
1. Set the period, choose the **Chart** (empty: the statutory one) and press **Show**.

Shows:
- Figures: Profit or Loss; A year before; Accounts on no line, when there are any. An account that sits on a balance sheet line is not counted here as on no line.
- Table: Line, Name, Amount, A year before, in the lines' tree.

Result: nothing changes. One chart (tree) at a time: the one chosen under **Chart**, else the company's statutory chart category (as the trial balance). Posting groups can post one entry to several trees through the account mapping, so trees are never added together. When no category is marked statutory and none is chosen, every chart is read. The result is the credit-side lines less the debit-side lines at the top of the tree.

Refused when: "The period ends before it starts."

API: `GET /reports/profit-loss?from=&to=&chart=`.

## Related

- [balance-sheet.md](balance-sheet.md)
- [../../accounting/statement-lines.md](../../accounting/statement-lines.md)

# Balance sheet lines and profit and loss lines

The lines of the two financial statements, in a tree. Which accounts a line holds is configuration, by the start of their codes, so a new account falls in by itself.

**Where:** Accounting setup › Balance sheet lines (`/config/balance-sheet-lines`, table `tbl_chart_assets_liabilities`) and Accounting setup › Profit and loss lines (`/config/profit-loss-lines`, table `tbl_chart_profit_loss`). **Who:** an administrator changes; see [configuration-lists.md](configuration-lists.md).

The screen shows these lists as a tree, because a line can sit under another.

## Columns

| Column | Meaning |
|--------|---------|
| Code, Name | What the statement prints. |
| Under | The heading above this line. |
| Sort order | The order among its neighbours. |
| Accounts | The starts of account codes, by commas (`31, 32, 35`). Empty on a heading. |
| Balance side | 0 any account, 1 only accounts with a debit balance, 2 only with a credit balance. A client in credit can so go to a liability line. |
| Credit side | Ticked: the line shows credit less debit (income, liabilities, equity). Not ticked: debit less credit. |
| Result kind | 0 from its accounts. 1 the result of the period. 2 the result of earlier years. Used on balance sheet lines. |

## How an account finds its line

- Among lines whose balance side the account meets, the longest start of code that fits wins.
- A line with a result kind takes no accounts.
- An account on no line of either statement is listed on the report as "Accounts on no line". It is never dropped.
- Each heading totals everything below it, at every level. A sub-line on the other side counts negative.

## Actions

### Add or change a line

Steps:
1. Press **Add**, or click a line.
2. Fill in the columns above and press **Save**.

Result: the next time the statement is shown it uses the new lines. Posted entries do not change.

Refused when: see [configuration-lists.md](configuration-lists.md).

API: `POST /config/balance-sheet-lines`, `POST /config/profit-loss-lines`.

### Switch a line off

Steps: open the line, press **Switch off…** and confirm.

Result: the statement leaves it out. Its accounts look for another line, or show under "Accounts on no line".

API: `DELETE /config/balance-sheet-lines/{id}`, `DELETE /config/profit-loss-lines/{id}`.

## Related

- [../reports/financial/balance-sheet.md](../reports/financial/balance-sheet.md)
- [../reports/financial/profit-and-loss.md](../reports/financial/profit-and-loss.md)
- [configuration-lists.md](configuration-lists.md)

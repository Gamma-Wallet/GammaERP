# Reports

Everything under the **Reports** menu, and the Dashboard at the top of the menu. This page holds what every report shares. Reports read posted data; none of them changes anything.

**Where:** Reports, with its folders Financial, VAT, and Stock and sales. **Who:** the report's item in your menu. Menu items are given to users and roles under Users and rights › Rights, and giving a folder gives everything under it. Administrators pass. The server checks this too: without the item the API answers `NotAllowedError`: "this screen is not in your menu."

| File or folder | What it covers |
|----------------|----------------|
| [dashboard.md](dashboard.md) | The dashboard |
| [dashboard-items.md](dashboard-items.md) | Which cards and charts it shows |
| [financial/](financial/README.md) | Statements, ledgers, ageing, open invoices |
| [vat/](vat/README.md) | VAT books and the two VAT returns |
| [stock-and-sales/](stock-and-sales/README.md) | Stock, movements, sales and purchases, contracts, clients |

## Actions

### Show a report

Steps:
1. Open the report from the menu. The period opens at this month so far; a report at one day opens at today.
2. Fill in the filters. Required ones are marked, and **Show** stays off until they are filled.
3. Press **Show**.

Result: the figures, a chart on some reports, and the table with its totals, 100 rows a page. A report opened from a link, such as a row of another report or a dashboard card, shows at once.

Refused when: "The period ends before it starts." on reports that check it.

### Open what stands behind a row

Steps:
1. Click a row.

Result: a row with a document opens the document; a journal opens in its sheet, under the document's own category. Some reports open another report instead; their pages say so. Other rows do nothing.

### Download Excel or PDF

Steps:
1. Show the report.
2. Press **Excel** or **PDF**. Only the reports below have the buttons, and they are off until the report is shown.

Result: a file of what the filters give. A PDF is in the language chosen at the top of the screens.

| Excel and PDF | Neither |
|---------------|---------|
| Trial balance, Account ledger, Client and supplier ledger, Cost centre ledger, VAT books, VAT return, Stock, Item movements, Sales and purchases by line, Client statistics | Balance sheet, Profit and loss, Ageing, Open invoices, Staff ledger, VAT return by boxes, Sales by waiter, Sales and purchases by client, Sales and purchases by cost centre, Transfers, Purchase price history, Document totals, Contracts |

### Language of a report

The API takes `language` on each call, and the screens send the one chosen at the top. Each company has its own language plus English, the default. Each country's chart keeps its own account names.

### Amounts

Amounts are in the company's currency (`Lcu`). Reversal types count negative where a report says so.

## Related

- [dashboard.md](dashboard.md)
- [financial/README.md](financial/README.md)
- [vat/README.md](vat/README.md)
- [stock-and-sales/README.md](stock-and-sales/README.md)

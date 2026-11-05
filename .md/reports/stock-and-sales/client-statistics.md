# Client statistics

Partners ranked by their posted documents of one document category, and one partner's own figures. Also called partner statistics.

**Where:** Reports › Stock and sales › Client statistics. **Who:** the `partner-statistics` report in your menu.

## Actions

### Show the ranking

Filters: **Document category** (required, one), **From**, **Up to**.

Steps:
1. Choose the category, such as Sales or Purchases. Set the period and press **Show**.

Shows:
- Figures: Partners, Total balance.
- Table: Partner, Documents, Debit, Credit, Balance, the largest debit plus credit first. The screen reads up to 500 partners.

Amounts come from the balance rows of the documents the request sees. When the company's currency has changed by the period's last day (see [year closing](../../accounting/year-closing.md)), each row is converted at its day's rate into the currency of the period's last day (today when Up to is empty), and the ranking is made from the converted amounts.

Result: nothing changes.

API: `GET /statistics/partners?category=&from=&to=&page=&pageSize=`, 500 a page at most. Excel: `/statistics/partners/xlsx`. PDF: `/statistics/partners/pdf`, with `category`, `from`, `to`. Excel holds the first 500.

### Open a partner's figures

Steps:
1. Click a partner in the ranking.

Result: under the table: Balance, First document, Last document, a chart of documents by month per category (value with VAT), and the top ten products.

Refused when: you have neither this report nor the partner register (Clients and suppliers) in your menu: `NotAllowedError: this screen is not in your menu.` Administrators pass.

API: `GET /partners/{id}/statistics?from=&to=`.

## Related

- [sales-and-purchases-by-client.md](sales-and-purchases-by-client.md)
- [../financial/client-and-supplier-ledger.md](../financial/client-and-supplier-ledger.md)
- [../../accounting/year-closing.md](../../accounting/year-closing.md)

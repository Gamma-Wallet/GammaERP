# Partner balances

What a partner owes the company, or the company owes it. Balances come only from posted documents: nobody types them. The balance is debit less credit, in Lcu.

**Where:** on each document's from and to side, and under Reports › Financial and Reports › Stock and sales. **Who:** each report in your menu. The balance of a partner on a document reads the partner's statistics, so it needs the partner register (Clients and suppliers), the Client statistics report, or a Documents screen in your menu: anyone who writes documents sees it.

Table: `tbl_partner_balance`, one row per line of a posted document. Which amount goes in is configuration: the document type's from or to side row names it (`BalanceValueElementsId`). It goes in as credit on the from side and as debit on the to side, for the line's own partner or else the document's. A journal line on a partner moves the balance too, when the document type × line type names its amounts.

## Actions

### See the balance on a document

Steps:
1. Open a document and choose the partner on its from or to side.
2. Read "Balance …" beside it.
3. Click it to open the partner's ledger.

Result: the balance in Lcu over the documents you may see.

API: `GET /partners/{id}/statistics`

### Read the ledger

Steps:
1. Open Reports › Financial › Client and supplier ledger.
2. Choose the partner and the period.

Result: the balance before the period as the first row, then each posted document with its date, category, debit, credit and running balance. Excel and PDF buttons download it.

API: `GET /partners/{id}/ledger?from={day}&to={day}`, also `/ledger/xlsx` and `/ledger/pdf`.

### Rank the partners

Steps:
1. Open Reports › Stock and sales › Client statistics.
2. Choose the document category and the period.
3. Click a partner to open its own figures.

Result: partners ranked by debit plus credit: documents, debit, credit, balance. The partner's figures are its months and products. Excel and PDF buttons download the ranking, at most 500 partners.

Refused when: the category is not given.

API: `GET /statistics/partners?category={id}&from={day}&to={day}&page=1&pageSize=500`, also `/xlsx` and `/pdf`. One partner: `GET /partners/{id}/statistics?from={day}&to={day}`.

What one partner's figures hold: the balance, the balance per currency, the first and last posting dates, figures per document category, figures per month, and the top 10 products by value.

### See open invoices and their age

Steps: open Reports › Financial › Open invoices or Ageing.

Result: see [payment days](payment-days.md).

API: `GET /reports/open-items`, `GET /reports/ageing`

### See goods a partner holds

Steps: open Reports › Stock and sales › Stock and choose partners as the holder.

Result: their running position: what they received is positive, what they delivered is negative.

API: `GET /reports/stock?holder=partner`

### Set the opening balances

Steps: use the Migration menu, step OPENING_PARTNERS.

API: `POST /migration/OPENING_PARTNERS/import`

## Correcting a balance

A balance is never edited. A posted document is corrected by a reversal, which makes its own balance rows. Payments cover an open invoice by hand, by its number.

## Related

- [Payments and open invoices](../documents/payments-and-open-invoices.md)
- [Reversing a document](../documents/reversing.md)
- [Payment days](payment-days.md)
- [Partners](partners.md)
- [General client](general-client.md)

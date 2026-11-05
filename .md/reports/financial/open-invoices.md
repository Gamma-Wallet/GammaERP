# Open invoices

The posted invoices not yet covered at a day, for clients and suppliers.

**Where:** Reports › Financial › Open invoices. **Who:** the `open-items` report in your menu.

A document is held open when its type has `holds_open_item`. A payment covers an invoice by hand: its line names the invoice's number and must cover all that is open on it. Nothing is matched automatically, and nothing closes in part. A payment line naming no document stays credit from the payer.

## Actions

### Show open invoices

Filters: **At**, a day. It opens at today. **Partner**, optional: only that client or supplier.

Steps:
1. Set **At**, optionally choose a **Partner**, and press **Show**.

Opened from a row of [Ageing](ageing.md), it keeps that row's day and partner.

Shows:
- Table: Document, Client or supplier, Side, Date, Due date, Total, Open amount, Days late.
- Total of the open amounts.

What is open: the invoice's total with VAT, less the payments linked to it (not those reversed), less its own reversals, all up to the day. Rows with nothing open are left out. The due date is the invoice's, or else its date; days late count from it.

Result: nothing changes. Clicking a row opens the document.

API: `GET /reports/open-items?to=&partner=`.

## Related

- [ageing.md](ageing.md)
- [client-and-supplier-ledger.md](client-and-supplier-ledger.md)

# Payment days

The days a partner has to pay. An invoice falls due that long after its date.

**Where:** Clients and suppliers, open a row, Details, Payment days. **Who:** the write right on the partner's category.

## Actions

### Set a partner's payment days

Steps:
1. Open the partner.
2. Type the days in Payment days. Empty counts as 0.
3. Click Save.

Result: invoices posted from now on use it. Invoices already posted keep their due date.

Refused when:
- "Payment days can't be below zero." The screen stops it, and the server refuses it too.
- Anything that refuses changing a partner.

API: `POST /partners` with `paymentDays`.

### Set a due date by hand on one invoice

Steps:
1. Open the document and fill Due date. Its hint says "Empty: the date plus the partner's payment days".
2. Post it.

Result: the date typed is kept.

API: `dueDate` on `POST /documents`.

### Let the due date fill itself at posting

Steps: leave Due date empty and post.

Result: for a document type with "holds open item" on, the due date becomes the document date, or else the posting date, plus the payment days of the partner on the document. In the base pack these are the sales and purchase invoices for goods and services.

API: `POST /documents/{id}/post`

### See what is late

Steps: open Reports › Financial › Open invoices or Ageing.

Result: Open invoices shows each due date and its days late. Ageing groups the open amounts per partner: not due, 1–30, 31–60, 61–90 and over 90 days late.

API: `GET /reports/open-items?to={day}&partner={id}`, `GET /reports/ageing?to={day}`

## Related

- [Partners](partners.md)
- [Balances](balances.md)

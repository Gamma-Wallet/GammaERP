# Payments and open invoices

How a payment closes an invoice. The person types the invoice number by hand, and a payment covers all that is open or is refused. Nothing is matched by itself: no first-in, no oldest-first.

**Where:** Documents › Cash desk or Bank › a Payment or Receipt › the Invoice number column. **Who:** as for saving and posting the document.

## What is open

A document is **open** when all of these hold:
- Its type holds open items (sales and purchase invoices in the base pack). An administrator sets this in System settings › Document types › the type › General, box "An invoice: it stays open until a payment covers it". The box "A payment: its lines name the invoices they cover" marks the types whose lines name invoices. See [Document types](../system-settings/document-types.md).
- It is posted, and a side is a partner.
- Its total, less payments that cover it, less its reversals, is above 0.

Open amounts are in Lcu. A client's invoice is owed to the company; a supplier's is owed by it. See the reports Open invoices and Ageing under Reports › Financial.

**Direction.** The side the partner stands on decides what a payment may cover:
- Money from the partner (partner on the From side, such as a Receipt) covers only what that partner owes the company.
- Money to the partner (partner on the To side, such as a Payment) covers only what the company owes them.

So a Payment naming a client's own sales invoice is refused, as is a Receipt naming a supplier's invoice.

## Actions

### Pay one invoice

Steps:
1. Start a **Payment** (money out) or **Receipt** (money in). The Invoice number column shows on types that pay open items.
2. Pick the cost centre and the client or supplier.
3. Add a line, and type the invoice's **Number**, exactly as the document shows it, with capitals.
4. Enter the amount: the whole open amount, in the line's total with VAT.
5. Post.

Result: the line is checked at posting. The payment is linked to the invoice, shows on its Linked documents tab, and the invoice leaves the open list. The usual posting also happens: see [posting.md](posting.md).

Refused when (at posting, all `OpenItemError`):
- There is no open document {number} of this client or supplier. Wrong number, another partner, already covered, the wrong direction (see Direction above), or dated after the payment: an invoice counts as open only on the payment's posting date.
- A payment covers the whole document: {number} has x open, the payment gives y. Compared to the cent, in Lcu.

Nothing closes in part. There is no partial cover.

API: `transactions[].referenceNumber` on `POST documents`, checked on `POST documents/{id}/post`.

### Pay several invoices with one payment

Steps:
1. Add one line per invoice, each naming its number and its whole open amount.
2. Post.

Result: each invoice is checked and linked. Several lines may name the same invoice: they are added up, and together must cover it.

Refused when: as above. One bad line refuses the whole payment.

### Take a payment with no invoice

Steps:
1. Leave the Invoice number empty.
2. Post.

Result: the money stays as credit from the payer on their balance. It covers no invoice, now or later. Prepayments and advances have no Invoice number column at all.

### Undo a payment

Steps:
1. Reverse the payment. See [reversing.md](reversing.md).

Result: its invoices are open again. Payments are reversed whole in the base pack.

### Set a due date

Steps:
1. On an invoice, type the **Due date**, or leave it empty.

Result: left empty, posting sets it to the document date (else the posting date) plus the partner's **Payment days**, set on the client or supplier. A due date never moves anything: it only shows how late an open invoice is, in Open invoices and Ageing: not due, 1–30, 31–60, 61–90, over 90 days.

API: `dueDate` on `POST documents`. Reports: `GET reports/open-items?to=&partner=`, `GET reports/ageing?to=` (with those report rights).

## Open points


## Related

- [Bank statements](bank-statements.md)
- [Cash desk](categories/cash-desk.md)
- [Bank](categories/bank.md)

# Repair bill

When the client comes back, the accepted offer becomes a draft sales invoice. Parts leave stock when it posts.

**Where:** Repairs › Repair board, on the card. **Who:** the screen `repairs`, and the invoice document type given to you or your role.

## Actions

### Make the bill

**Steps**
1. Press Make the bill. It shows when the offer is accepted and no invoice exists yet.
2. The draft opens in the document editor.

**Result:** a draft sales invoice (the pack's Sales invoice, services) copied from the offer, with the same lines, client and sides, dated today and linked to the offer. It is **unposted**: no stock and no balances move yet. The card shows the invoice's code, which opens it.

**Refused when**
- "the client has not accepted it.": the flow sets `needs_acceptance` and the offer is not accepted. This is checked by every flow, so the document screen refuses too.
- "this offer makes no invoice.": no active flow from the offer type sets `needs_acceptance`.
- "This job has no offer yet."
- "a document of this type was already made from it.": the pack's flow has `Once` on, so one invoice per offer.
- "that document type has no {kind} rows."
- Approval refusals, when the offer waits for approval.

**API:** `POST /repairs/{id}/bill`. Answers with the document id and category.

### Post the bill

**Steps**
1. In the draft, change anything needed. Each goods line needs the place the part leaves from.
2. Post it as any document.

**Result:** the parts on goods lines leave stock, repair time on service lines moves no stock, and the client owes the amount. Posting follows the document type's posting scenario.

**Refused when:** the normal posting refusals, such as a closed period or short stock.

**API:** `POST /documents/{id}/post`

### Take the payment

Payment is the normal money document made in the Cash desk or Bank documents. A repair shop's settings set no shift, and this board has no pay screen.

## Related

- [offer.md](offer.md)
- [board.md](board.md)
- [setup.md](setup.md)

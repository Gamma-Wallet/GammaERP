# Repair offer

An offer of parts and repair time made from the intake, marked accepted or refused by a person after asking the client.

**Where:** Repairs › Repair board, on the card. **Who:** the screen `repairs`, and the offer document type given to you or your role.

## Actions

### Make the offer

**Steps**
1. Press Make the offer on a card with no offer yet.
2. In the opened document, write the parts and the repair time as lines. The pack's Repair offer takes goods, service and account lines.
3. Save.

**Result:** a Repair offer made from the intake, unposted, with the same partner and sides and a copy of the intake's lines, dated today and linked to the intake. Its type books nothing: it moves no stock and no balances. Post it to make it final: the invoice is made only from a posted offer. The card shows its code and the buttons Accepted and Refused.

**Refused when**
- "the intake makes no offer that can be accepted.": no flow goes from the intake type to a type that sets `can_be_accepted`.
- "that document type has no {kind} rows.": the offer type does not hold the kinds of row the intake has.
- Approval refusals, when the intake waits for approval.
- "The repair does not exist."

**API:** `POST /repairs/{id}/offer`. Answers with the document id and category.

The pack's flow from intake to offer has `Once` off, so a second offer can be made from the document screen. The job's offer, on the card and in every action here, is the latest document made from the intake whose type can be accepted (`can_be_accepted`): the same rule that makes it. Other documents made from the intake are not taken as the offer.

### Mark it accepted

**Steps:** press Accepted on the card after the client agrees.

**Result:** the offer records who accepted and when, and any refusal is cleared. The card shows *Accepted* and offers Make the bill.

**Refused when**
- "This job has no offer yet."
- `NotAllowedError`: the offer has an invoice; the decision can't change.
- "The repair does not exist."

**API:** `POST /repairs/{id}/decide?accepted=true`

### Mark it refused

**Steps:** press Refused on the card. The button is hidden once the job has an invoice, and the server refuses it then too.

**Result:** the offer records who refused and when, and any acceptance is cleared. The job stays in its stage. A person moves it, for example to the pack's Refused stage. The client can change their mind: press Accepted afterwards.

**Refused when:** as for Accepted, including `NotAllowedError`: the offer has an invoice; the decision can't change.

**API:** `POST /repairs/{id}/decide?accepted=false`

### Change a decided offer

**Saving the offer clears its decision.** Every save of a document empties who accepted or refused it and when. The client answered the old lines, so after an edit the card goes back to undecided and must be marked Accepted again before billing.

**Steps**
1. Open the offer from its code on the card, change it and save.
2. Ask the client and press Accepted or Refused again.

## Related

- [bill.md](bill.md)
- [board.md](board.md)
- [setup.md](setup.md)


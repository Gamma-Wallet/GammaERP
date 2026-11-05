# Repair board

One column per stage, one card per job. Stages are moved by hand; no step moves them.

**Where:** Repairs › Repair board. **Who:** the screen `repairs`. The base pack gives it to the role Technician.

## Actions

### Read the board

Columns come in stage order, each with its number of cards. A card shows:
- The client, and the item if the intake has one.
- The intake's code, which opens it.
- *Come back on*, if the intake has a date.
- The technician and the stage, each a list you can change.
- The offer's code and *Accepted* or *Refused*, once there is an offer.
- The invoice's code, once there is one.
- The next action: Make the offer, Accepted, Refused or Make the bill.

Open jobs always show. Jobs in a final stage show for 30 days from the job row's `CreatedOn`, then leave the board.

The board shows all the company's jobs, not those of one repair shop or technician.

**Refused when:** "Repairs belong to a company: choose one of its nodes."

**API:** `GET /repairs/`

### Move a job to a stage

**Steps:** choose the stage in the list on the card.

**Result:** the job moves to that column. Nothing else changes: no document, stock or decision. Any stage can be chosen at any time.

**Refused when**
- "The stage does not exist."
- "The repair does not exist."

**API:** `POST /repairs/{id}/move/{stage}`

## Stages in the base pack

| Stage | Order | Final |
|-------|-------|-------|
| Received | 10 | No |
| Offer given | 20 | No |
| Accepted | 30 | No |
| Ready | 40 | No |
| Collected | 50 | Yes |
| Refused | 60 | Yes |

A new job starts in the first stage. Change them in [setup.md](setup.md).

The stages Accepted and Refused are not tied to the offer's decision: pressing Accepted on a card does not move it to that column.

## Related

- [new-job.md](new-job.md)
- [offer.md](offer.md)
- [bill.md](bill.md)
- [setup.md](setup.md)

## Open points

- A job has no delete action on the board. It leaves when it reaches a final stage and 30 days pass.

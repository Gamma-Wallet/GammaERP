# Repair documents

The documents of a repair job: the intake and the offer. The job itself lives on the repair board, and the invoice is a normal sales invoice.

**Where:** Documents › Repairs, and Repairs › Repair board. **Who:** for the documents see [who may do what](../life-cycle.md#who-may-do-what). The board needs the Repair board screen in your menu.

## Types in the base pack

| Type | Code | From | To | Flag | Can make |
|------|------|------|----|------|----------|
| Repair intake | `REPAIR_INTAKE` | Cost centre | Client | Starts a repair | Repair offer, more than once |
| Repair offer | `REPAIR_OFFER` | Cost centre | Client | Can be accepted | Sales invoice, services, once, after acceptance |

Both are never posted and take lines of kind Item, Service item and Account.

The intake's properties in the base pack: Item, Brand and model, Serial number or plate, Accessories left, Defect described, Come back on. The board shows the first text property as the item, and the first date property as the come-back date.

## Actions

### Start a job

Steps:
1. On Repairs › Repair board, find the client by name or phone, or type a name or phone.
2. Pick a technician if known and press **New job**.
3. Fill the intake's properties in the document that opens, and save.

Result: an intake document dated today, and a job in the first stage. A client from a typed name or phone is made like the company's general client.

Refused when:
- `NotConfiguredError`: no document type starts a repair.
- `NotConfiguredError`: the repair board has no stages.
- Give the client's name or phone.
- `NotConfiguredError`: the company has no general client to model a client on.

API: `GET repairs/clients?search=`, `POST repairs`.

### Make the offer

Steps:
1. On the card press **Make the offer**, then write the parts and repair time in the offer that opens.

Result: a repair offer linked to the intake.

Refused when: `NotConfiguredError`: the intake makes no offer that can be accepted.

API: `POST repairs/{id}/offer`.

### Mark the client's answer

Steps:
1. Press **Accepted** or **Refused** on the card.

Result: the offer records who and when. A person can change it until there is an invoice. Nothing moves the card: stages are set by hand.

Refused when:
- "This job has no offer yet."
- `NotAllowedError`: the offer has an invoice; the decision can't change.

API: `POST repairs/{id}/decide?accepted=true`.

### Make the bill

Steps:
1. On an accepted card press **Make the bill**.
2. Check the draft invoice and post it.

Result: a draft Sales invoice, services. Parts leave stock when it posts.

Refused when:
- `NotConfiguredError`: this offer makes no invoice.
- "the client has not accepted it." Saving a changed offer clears the acceptance and the refusal: mark it again.
- "a document of this type was already made from it."

API: `POST repairs/{id}/bill`.

### Move a job or give it a technician

Steps:
1. Pick the stage or the technician on the card.

Result: the job changes. The base pack's stages: Received, Offer given, Accepted, Ready, Collected, Refused. Stages are under Repairs › Repair stages. The board shows final-stage jobs for 30 days.

API: `POST repairs/{id}/move/{stage}`, `POST repairs/{id}/technician?technician=`, `GET repairs`.

## Related

- [Flows](../flows.md)
- [Sales](sales.md)

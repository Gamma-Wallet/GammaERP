# New repair job

Starting a job: finding or making the client, the intake document, and the technician.

**Where:** Repairs › Repair board, the form at the top. **Who:** the screen `repairs`, and the intake document type given to you or your role.

## Actions

### Find a client

**Steps**
1. Type in *Find a client*, by name or phone. Results come as you type.
2. Press the client's button.

**Result:** the client is chosen and the Name and Phone fields hide. Up to ten clients show. A phone matches on the client's property with the Code `PHONE` (see [Property codes the system reads](../system-settings/types-and-categories.md#property-codes-the-system-reads)). Typing again clears the choice.

**API:** `GET /repairs/clients?search=`

### Start a new job

**Steps**
1. Find a client, or type a Name, a Phone or both.
2. Optionally choose a Technician: an active employee of the company that you see.
3. Press New job. It is disabled until there is a client, a name or a phone.

**Result**
- With a typed name or phone, a new client is made. It takes the kind and categories of the company's general client. With only a phone, the phone is also the name. The phone is stored in the type's property with the Code `PHONE`, if the type has one.
- A Repair intake document is saved for the client, dated today, with no lines.
- A job is made in the first repair stage.
- The intake opens in the document editor for you to fill in.

**Refused when**
- "no document type starts a repair.": no active type of the company sets `starts_repair`.
- "the repair board has no stages.": add one in Repairs › Repair stages.
- "The client does not exist."
- "Give the client's name or phone."
- "The technician is not an employee of this company.": the technician is not an active employee you see.
- "the company has no general client to model a client on.": no settings row names a walk-in client. See [setup.md](setup.md).
- "this document type has not been given to you or to one of your roles."
- "Repairs belong to a company: choose one of its nodes."

**API:** `POST /repairs/` with `partnerId`, `name`, `phone`, `technicianId`. Answers with the document id and category.

### Fill the intake

The intake's properties are what the client left. The base pack's Repair intake has these, all optional:

| Property | Kind | Shown on the board |
|----------|------|--------------------|
| Item | Text | Yes, as the card's item |
| Brand and model | Text | No |
| Serial number or plate | Text | No |
| Accessories left | Text | No |
| Defect described | Text | No |
| Come back on | Date | Yes, as *Come back on* |

The board shows the first text property with a value as the item, and the first date as the come-back date, so each company's own properties decide. Properties are in `tbl_document_type_info` and are edited on the document type.

**Steps**
1. In the opened intake, fill the properties. Lines can be added; the type allows them.
2. Save.

**Result:** the document is saved. Its type books nothing (`NeverPosted`): it moves nothing, and posting it only makes it final.

**API:** `POST /documents`

### Give a job its technician

A technician is an employee in Company › Employees, not a user. The list holds the employees of the company and of the places you see.

**Steps:** choose a technician on the card. Choosing none clears it.

**Result:** the job shows the technician. The board is not filtered by technician, and the choice gives no rights.

**Refused when**
- "The repair does not exist."
- "The technician is not an employee of this company.": the technician is not an active employee you see.

**API:** `POST /repairs/{id}/technician?technician=`

## Related

- [offer.md](offer.md)
- [board.md](board.md)
- [setup.md](setup.md)

## Open points

- The client search reads all clients the user can see in configuration, not only those of the repair shop.

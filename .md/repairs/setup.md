# Repairs setup

What decides how repairs work: stages, the Technician role, and settings on document types and flows. All of it is configuration in the database.

**Where:** Repairs › Repair stages; System settings › Document types; System settings › Document flows. **Who:** anyone with the menu item can read; only an administrator changes configuration.

## Repair stages

### Add or change a stage

Stages are rows in `tbl_repair_stage`, per company.

**Steps**
1. Open Repairs › Repair stages.
2. Add or edit a row: name, sort order, and whether it is final.
3. Save.

**Result:** the board's columns follow. The lowest sort order is where a new job starts. A final stage's jobs leave the board after 30 days.

**Refused when:** you are not an administrator ("only an administrator changes the configuration").

**API:** `GET /config/repair-stages`, `POST /config/repair-stages`, `DELETE /config/repair-stages/{id}`

## The Technician role

The role `TECHNICIAN` (`tbl_user_category`) comes with the base pack. The pack gives it:

- the menu item Repairs › Repair board (`tbl_inter_menu_user`);
- the document types Repair intake, Repair offer and Sales invoice, services (`tbl_inter_document_type_user`);
- Write on the document categories Repairs and Sales, and on the partner category Clients, because New job makes the client (`tbl_elements_access`).

An administrator can change these in Users and rights › Rights and Roles, and on the users tab of System settings › Document types.

### Set up a technician user

**Steps**
1. Give the user the role in Users and rights › Users.
2. If the company's intake, offer or invoice type is not one the pack gives, give it to the role in System settings › Document types, on the users tab.
3. Add the person in Company › Employees, so they can be chosen on a job.

**Result:** the user opens the board and runs a job from New job to a posted invoice. Without a type, New job, Make the offer and Make the bill are refused with "this document type has not been given to you or to one of your roles."

**API:** `PUT /documents/types/{id}/users` with `userIds`, `roleIds`.

The technician on a job is an employee. The role is a user's. Nothing links the two.

## Document type settings

**Where:** System settings › Document types › the type › General tab. **Who:** an administrator.

| Column (`tbl_document_type`) | Screen label | Meaning | Base pack |
|------------------------------|--------------|---------|-----------|
| `starts_repair` | A new repair job opens this type | New job makes a document of this type | Repair intake |
| `can_be_accepted` | Can be marked accepted or refused by the client | Make the offer looks for a flow to a type with this set | Repair offer |

Both pack types also set `NeverPosted` (*Posting books nothing (offers, contracts, orders)*).

**Steps:** tick the box and press Save.

**API:** `GET /documents/types/{id}/settings`, `PUT /documents/types/{id}/settings`

## Flow setting

**Where:** System settings › Document flows. Rows in `tbl_document_type_flow`.

| Column | Meaning |
|--------|---------|
| `needs_acceptance` | The source document must be accepted before this flow can be taken. Checked wherever a flow is used. Make the bill also looks for this flow to find the invoice type |

| Flow in the base pack | `needs_acceptance` | `Once` |
|-----------------------|--------------------|--------|
| Repair intake to Repair offer | No | No |
| Repair offer to Sales invoice, services | Yes | Yes |

**Steps**
1. Open System settings › Document flows and edit the row.
2. Save.

**Refused when:** you are not an administrator.

**API:** `GET /config/document-type-flows`, `POST /config/document-type-flows`

## General client

Making a client from a name or phone copies the kind and categories of the walk-in client in `tbl_point_of_sale_setting`. The base pack sets the General client. See [../point-of-sale/settings.md](../point-of-sale/settings.md).

## Repair shop

Add a cost centre of the kind Repair shop so that the point of sale opens the board. See [../point-of-sale/repair-shop.md](../point-of-sale/repair-shop.md). The board works without one: it is reached from the Repairs menu.

## Related

- [overview.md](overview.md)
- [board.md](board.md)
- [offer.md](offer.md)
- [bill.md](bill.md)

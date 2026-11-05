# Approval limits

Up to what total a role's documents pass without approval, and up to what total the role may approve. A company with no such rows approves nothing.

**Where:** Users and rights › Approval limits. **Who:** anyone with it in their menu reads. An administrator changes rows. The screen's generic actions are in [Configuration screens](../system-settings/configuration-screens.md).

Configuration: `tbl_approval_limit`, model `approval-limits`.

| Field | Column | Meaning |
|---|---|---|
| Role | `RoleId` | The role. |
| Limit | `Limit` | The largest total, with VAT, in the company's currency. |

## How it works

- It applies to document types with `NeedsApproval`. In the pack: `PURCHASE_REQUEST` and `PURCHASE_ORDER`. An administrator sets it in System settings › Document types › the type › General, box "Over the limit of its writer it waits for approval".
- A document is over the limit when its total is above the highest limit of the roles of the user who wrote it: the user of its first version, from the row history, not whoever saved it last. A writer with no limit stands at 0.
- A document over the limit can't be posted or become the next document until it is approved by hand: "it waits for approval."
- An administrator may approve any document. Anyone else may approve when one of their roles has a limit and their highest limit covers the total.
- Changing a document clears its approval.

## Actions

### Set a limit

Steps:

1. Press **Add**, or press a row.
2. Choose the Role and type the Limit.
3. Press **Save**.

Result: the limit applies from the next check. Documents wait in Documents › Approvals.

Refused when: as in [Configuration screens](../system-settings/configuration-screens.md).

API: `GET /config/approval-limits`, `POST /config/approval-limits`

### Remove a limit

Steps:

1. Press the row, then **Switch off...**
2. Confirm.

Result: the row stops counting. With no rows left, nothing in the company waits.

API: `DELETE /config/approval-limits/{id}`

## Related

- [Approvals](../documents/approvals.md)
- [Roles](roles.md)
- [Document types](../system-settings/document-types.md)

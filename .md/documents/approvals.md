# Approvals

A document of a type that needs approval, over the limit of the role of its writer, can't be posted or become the next document until someone approves it by hand.

**Where:** Documents › Approvals. Limits: Users and rights › Approval limits. **Who:** the Approvals screen in your menu, and a limit that covers the document, or an administrator. Administrators write the limits.

## How it works

- **Which types:** those flagged "needs approval" (`tbl_document_type.needs_approval`). In the base pack: Purchase request and Purchase order. An administrator sets it in System settings › Document types › the type › General, box "Over the limit of its writer it waits for approval".
- **Limits:** a row per role (`tbl_approval_limit`): the largest total, with VAT, in Lcu. The limit is for the whole company, not per type. The base pack has none.
- **A company with no limits approves nothing:** nothing waits.
- **The writer** is the user of the document's first version, read from the row history. A person who only saved it later does not count as the writer, so a higher limit of theirs does not pass it.
- **A document waits** when its total is over the highest limit among the writer's roles. A writer whose roles have no limit counts as 0, so anything above 0 waits.
- **What it stops:** posting it, and making the next document from it. Both are refused with `NotAllowedError`: "it waits for approval."
- **Changing clears it:** saving an approved document clears the approval, and it is judged again.

## Actions

### Set a limit

Steps:
1. Open Users and rights › Approval limits.
2. Add a row: the role and the limit.
3. Save.

Result: from now on the rule applies to this company's documents of those types. The first limit a company adds makes every role without a row count as 0.

Refused when: you are not an administrator.

API: `GET config/approval-limits`, `POST config/approval-limits`, `DELETE config/approval-limits/{id}`.

### See what waits

Steps:
1. Open Documents › Approvals.

Result: the documents waiting, oldest first: Document, Document type, Client or supplier, Date, Written by (the writer of the first version), Total. "Nothing waits for approval." when empty.

API: `GET approvals`. Each row has `canApprove`.

### Approve a document

Steps:
1. Press **Approve** on the row. It is off, with "Over your approval limit", when your limit does not cover the total.

Result: the document leaves the list, its approver and time are recorded, and it can be posted and the flow can be taken. Nothing else changes.

Refused when:
- "This document does not wait for approval.": its type needs no approval, it is already approved, or its total is within the writer's limit.
- `NotAllowedError`: this document is over your approval limit. You may approve when one of your roles has a limit and your highest limit is at least the total.
- The document does not exist.

API: `POST approvals/{id}`.

## Not possible

- There is no refuse button. A person who does not approve simply leaves it, or the writer changes it or switches it off.

## Related

- [Flows](flows.md)
- [Orders](categories/orders.md)

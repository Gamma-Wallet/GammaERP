# Document flows

Which document type a document may make: offer to contract, offer to invoice, contract to invoice. The new document starts unposted with the source's lines.

**Where:** System settings › Document flows. **Who:** anyone with it in their menu reads. An administrator changes rows. The screen's generic actions are in [Configuration screens](configuration-screens.md).

Configuration: `tbl_document_type_flow`, model `document-type-flows`. The base pack has 26 rows.

| Field | Column | Meaning |
|---|---|---|
| Source type | `SourceTypeId` | The document type that makes. |
| Target type | `TargetTypeId` | The document type made. |
| Once | `Once` | A document makes only one of the target type: an offer is taken up once, a contract is billed again and again. |
| Needs acceptance | `NeedsAcceptance` | The source must be marked accepted first. |
| Valid until info | `ValidUntilInfoId` | The source's date property after which it makes nothing more, such as a contract's end date. Empty: no limit. |

## Actions

### Add or change a flow

Steps:

1. Press **Add**, or press a row.
2. Choose the fields.
3. Press **Save**.

Result: documents of the source type offer the target type.

Refused when:

- "Define the document type's transactions first." The source type holds no transaction type.
- `This document type does not have the source's <names> transactions.` The target must hold every transaction type the source holds, so set them first in [Document type transactions](document-type-transactions.md).
- as in [Configuration screens](configuration-screens.md).

API: `GET /config/document-type-flows`, `POST /config/document-type-flows`, `DELETE /config/document-type-flows/{id}`

### What a person sees on a document

The flow is used by hand, on the document. See [Flows](../documents/flows.md).

A flow can't be taken now when:

- the source waits for approval;
- it needs acceptance and the client has not accepted it;
- the target type has no rows of a kind the document holds;
- Once is set and a document of this type was already made from it;
- the date in Valid until info has passed.

## Related

- [Flows](../documents/flows.md)
- [Document types](document-types.md)
- [Document type transactions](document-type-transactions.md)

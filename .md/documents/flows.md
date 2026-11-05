# Flows: one document from another

A document can make the next document of a chain, offer to contract to invoice, and can be repeated for another day. Linked documents show the chain.

**Where:** a saved document › **Generate**, **Repeat**, and the Linked documents tab. **Who:** see each action. Flows are configured under System settings › Document flows (`tbl_document_type_flow`).

## Actions

### Make the next document

Steps:
1. Open the document and press **Generate**.
2. Pick the type. On a contract type billed by a plan, the contract's own type is not offered: use **Renew** under [Renewals](../contracts/renewals.md). A greyed item can't be taken now: hover for the reason, or click and it shows at the top.
3. An unsaved document is saved first.

Result:
- A new draft of the target type opens, to check and post.
- Copied: the lines, the from and to sides, the payment method.
- Set: document and posting date today, a new number, a link to the source.
- Carried over: the [opportunity](../crm/opportunity-on-documents.md) of the source.
- Not copied: the type's properties, their number, the due date.
- The source does not change, with one exception: an invoice made from a contract with a [billing plan](../contracts/billing-plan.md) moves the plan's next date on by its months, in the same transaction. A document of the contract's own type (a renewal) does not move it.

Refused when (each reason shows on the item, and the API gives it as `NotAllowedError`):

| Reason | Flow column |
|--------|-------------|
| it waits for approval. | See [approvals.md](approvals.md) |
| the client has not accepted it. | `NeedsAcceptance` |
| post it first: an invoice is made only from a posted document. | The source's type books nothing (offer, contract, order) and the target holds an open item (an invoice) |
| that document type has no {kind} rows. | The target must take the same transaction types as the source's lines |
| a document of this type was already made from it. | `Once`: an offer is taken up once; a contract is billed again and again |
| it ended on {date}. | `ValidUntilInfoId`: the source's date property, for example a contract's End date |

Also refused:
- `NotConfiguredError`: this document does not make documents of that type.
- Every refusal of a save on the new document, such as the target type not given to you, or stock: see [drafts.md](drafts.md).

API: `GET documents/{id}/flows` lists the targets with `refused`. `POST documents/{id}/make/{type}` makes one.

### Flows in the base pack

| From | To | Once |
|------|----|------|
| Sales offer, goods | Sales contract, goods; Sales invoice, goods; Sales order | Yes |
| Sales offer, services | Sales contract, services; Sales invoice, services | Yes |
| Purchase offer, goods | Purchase contract, goods; Purchase invoice, goods; Purchase order | Yes |
| Purchase offer, services | Purchase contract, services; Purchase invoice, services | Yes |
| Sales or purchase contract, goods or services | The invoice of the same kind | No, until the End date |
| Point-of-sale contract | Point-of-sale receipt | No, until the End date |
| Each contract | A contract of its own type: the renewal, made by Renew | Yes |
| Purchase request | Purchase order | Yes |
| Purchase order | Purchase invoice, goods | Yes |
| Sales order | Sales invoice, goods | Yes |
| Point-of-sale order | Point-of-sale receipt | No |
| Repair intake | Repair offer | No |
| Repair offer | Sales invoice, services, once accepted | Yes |

### Repeat a document

Done again for another day, for recurring journals and anything else. Any saved document, draft or posted, shows the button.

Steps:
1. Press **Repeat**.
2. Set the date. Today is proposed.
3. Tick **Post it straight away** if wanted.
4. Press **Repeat**.

Result:
- A copy of the document, of the journals under it, and of their lines, dated that day. Their number is left empty, and the properties are kept.
- It is linked to what it repeats and opens on the screen.
- When asked, it is posted, a sheet with its journals. Otherwise it stays a draft.
- Nothing repeats by itself: a person does this each time.

Refused when: every rule of a new document applies. A reversal can't be repeated (`ReversalError`), and a point-of-sale order is not posted. All or nothing.

API: `POST documents/{id}/repeat` with `{ "date": "2026-11-02", "post": false }`.

### See linked documents

Steps:
1. Open the **Linked documents** tab. It shows when the document has any.

Result: buttons to open "Reverses {number}", each document it was created from, and each document made from it, with "(draft)" where unposted. A payment's link to the invoices it covers shows here too.

API: `GET documents/{id}` gives `createdFromIds` and `reversedDocumentId`. `GET documents/{id}/made` lists what was made from it.

## Related

- [The Contracts menu](../contracts/README.md)
- [Approvals](approvals.md)
- [Offers](categories/offers.md)

# Orders

Purchase requests, purchase orders and sales orders. Kept as documents. Posting one books nothing; it makes the order final, and an invoice is made only from a posted order. Purchase documents can need approval before they go on.

**Where:** Documents › Orders. **Who:** see [who may do what](../life-cycle.md#who-may-do-what).

## Types in the base pack

| Type | Code | From | To | Needs approval | Can make |
|------|------|------|----|----------------|----------|
| Purchase request | `PURCHASE_REQUEST` | Supplier | Cost centre | Yes | Purchase order |
| Purchase order | `PURCHASE_ORDER` | Supplier | Cost centre | Yes | Purchase invoice, goods |
| Sales order | `SALES_ORDER` | Cost centre | Client | No | Sales invoice, goods |
| Point-of-sale order | `POS_ORDER` | Cost centre | Client | No | Point-of-sale receipt |

All take lines of kind Item, Fixed asset, Service item and Account. Purchase offer, goods can also make a Purchase order, and Sales offer, goods a Sales order.

## Actions

### Write a request or an order

Steps:
1. Press **New** and pick the type.
2. Pick the sides and add lines.
3. Save. There is no Post button.

Result: a stored document. It moves no stock, balance or account.

Refused when: see [saving](../drafts.md#save).

API: `POST documents`.

### Take an order on

Steps:
1. If the document is over its writer's limit, have it approved: see [approvals](../approvals.md).
2. Use **Generate** and pick the next type. See [flows](../flows.md).

Result: a draft of the next type with the same lines, linked. Each order makes one of the next type.

Refused when:
- "it waits for approval."
- "a document of this type was already made from it."

API: `POST documents/{id}/make/{type}`.

### Point-of-sale orders

Waiters send these from the point of sale, and they are billed at payment. A point-of-sale order can make receipts more than once. See the Point of sale documentation.

## Related

- [Approvals](../approvals.md)
- [Flows](../flows.md)
- [Purchases](purchases.md)

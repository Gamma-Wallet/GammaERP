# Sales documents

Invoices to clients, and the receipts of the point of sale. Posting takes stock out and moves the client's balance.

**Where:** Documents › Sales. **Who:** see [who may do what](../life-cycle.md#who-may-do-what).

## Types in the base pack

| Type | Code | From | To | Kinds of line | Open item | Partial reversal | Reversed by |
|------|------|------|----|---------------|-----------|------------------|-------------|
| Sales invoice, goods | `SALES_INVOICE_GOODS` | Cost centre | Client | Item, Fixed asset, Account | Yes | Yes | Credit note, goods sold |
| Sales invoice, services | `SALES_INVOICE_SERVICES` | Cost centre | Client | Account, Item, Fixed asset, Service item | Yes | Yes | Credit note, services sold |
| Point-of-sale receipt | `POS_RECEIPT` | Cost centre | Client | Service item, Item | No | Yes | `POS_REVERSAL` |
| Reversal of a point-of-sale receipt | `POS_REVERSAL` | Client | Cost centre | Item, Service item | No | | Is a reversal |

Item and Fixed asset lines move stock. Service item and Account lines do not.

## Actions

### Write a sales invoice

Steps:
1. Press **New** and pick the type.
2. Pick the place in **From**, the client in **To**. The client's balance shows beside it.
3. Add lines. The sale price is proposed: see [lines](../lines.md#pick-the-item).
4. Save, look at the Posting tab, post.

Result: at posting the stock leaves the place at its oldest cost, the client's balance and the accounts move, and the invoice stays open until a payment covers it. See [payments](../payments-and-open-invoices.md).

Refused when: see [saving](../drafts.md#save) and [posting](../posting.md#post).

API: `POST documents`, `POST documents/{id}/post`.

### Make an invoice from another document

Steps:
1. Open the offer, order, contract or accepted repair offer, and use **Generate**. See [flows](../flows.md).

Result: a draft invoice with the source's lines.

API: `POST documents/{id}/make/{type}`.

### Sell a fixed asset

Steps:
1. Add a **Fixed asset** line with the asset. Its remaining value is proposed as the price, and can be changed.
2. Post.

Result: a normal sale of the product at the asset's actual value. On types that sell assets (these three in the base pack), posting marks the asset sold in every depreciation book, the tax book too. It then shows as Disposed and leaves Remaining asset value; a reversal brings it back. A transfer to a client does not sell it: the asset stays the company's. See [selling an asset](../../fixed-assets/selling-an-asset.md).

### Sell to a company of the group

Steps:
1. Post a sale to the partner that stands for a group company.

Result: where Company › Purchases from the group pairs the sale type with a purchase type, the posting makes an unposted purchase in that company's books, for that company to check and post. Reversing the sale changes the draft to what is left of the sale, or to nothing. See [purchases](purchases.md).

### Receipts

Point-of-sale receipts come from the point-of-sale screens. The point of sale never identifies a customer: a general client stands in for them. Posting one needs an open shift where the point requires it. See the Point of sale documentation.

## Open points


## Related

- [Reversals](reversals.md)
- [Payments and open invoices](../payments-and-open-invoices.md)

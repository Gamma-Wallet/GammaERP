# Offers

Prices offered to a client or asked of a supplier. An offer is kept as a document. Posting it books nothing; it makes the offer final. It can become a contract, an order or, once posted, an invoice.

**Where:** Documents › Offers. **Who:** see [who may do what](../life-cycle.md#who-may-do-what).

## Types in the base pack

| Type | Code | From | To | Kinds of line | Can make |
|------|------|------|----|---------------|----------|
| Sales offer, goods | `SALES_OFFER_GOODS` | Cost centre | Client | Item, Fixed asset, Service item, Account | Sales contract, goods; Sales invoice, goods; Sales order |
| Sales offer, services | `SALES_OFFER_SERVICES` | Cost centre | Client | Item, Service item, Account | Sales contract, services; Sales invoice, services |
| Purchase offer, goods | `PURCHASE_OFFER_GOODS` | Supplier | Cost centre | Item, Fixed asset, Service item, Account | Purchase contract, goods; Purchase invoice, goods; Purchase order |
| Purchase offer, services | `PURCHASE_OFFER_SERVICES` | Cost centre | Cost centre | Item, Service item, Account | Purchase contract, services; Purchase invoice, services |

Purchase offer, services has a required property, Offer valid until.

## Actions

### Write an offer

Steps:
1. Press **New** and pick the type.
2. Pick the sides and add lines, as on an invoice. An **Opportunity** of the CRM can be linked.
3. Save.
4. Press **Post** to make it final. Posting books nothing: no entry, no stock, no invoice. From then on it can't be changed, and invoices are made from it. It can still be switched off while nothing active was made from it.

Result: a stored document. It writes no entries and moves no stock, balance or account.

Refused when: see [saving](../drafts.md#save). Posting: see [posting.md](../posting.md#post-an-offer-a-contract-or-an-order).

API: `POST documents`.

### Take up an offer

Steps:
1. Open the offer and use **Generate**. See [flows](../flows.md).

Result: a draft of the next type with the offer's lines, linked to it. Each target type can be made once from one offer.

Refused when: "a document of this type was already made from it."

API: `POST documents/{id}/make/{type}`.

## Related

- [Flows](../flows.md)
- [Contracts](contracts.md)
- [Orders](orders.md)

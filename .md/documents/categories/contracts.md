# Contract documents

Contracts with clients and suppliers, kept as documents. Posting one books nothing; it makes the contract final. A posted contract makes invoices again and again until its end date.

**Where:** Documents › Contracts. **Who:** see [who may do what](../life-cycle.md#who-may-do-what). Billing and renewals: [the Contracts menu](../../contracts/README.md).

## Types in the base pack

| Type | Code | From | To | Bills to |
|------|------|------|----|----------|
| Sales contract, goods | `SALES_CONTRACT_GOODS` | Cost centre | Client | Sales invoice, goods |
| Sales contract, services | `SALES_CONTRACT_SERVICES` | Cost centre | Client | Sales invoice, services |
| Purchase contract, goods | `PURCHASE_CONTRACT_GOODS` | Supplier | Cost centre | Purchase invoice, goods |
| Purchase contract, services | `PURCHASE_CONTRACT_SERVICES` | Supplier | Cost centre | Purchase invoice, services |
| Point-of-sale contract | `POS_CONTRACT` | Cost centre | Client | Point-of-sale receipt |

Kinds of line: Item, Service item, Account, Fixed asset. The point-of-sale contract takes Item and Service item.

Properties: Start date and End date on all; Last payment date on the point-of-sale contract. Each flow to an invoice stops at the type's End date.

## Actions

### Write a contract

Steps:
1. Press **New** and pick the type, or make it from an offer with **Generate**.
2. Pick the sides, set the Start date and End date, add lines.
3. Save. An **Opportunity** can be linked.
4. Press **Post** to make it final. Posting books nothing: no entry, no stock, no invoice. From then on it can't be changed, and invoices are made from it. It can still be switched off while nothing active was made from it. The billing plan stays open to change.

Result: a stored document. It writes no entries. Once saved, the billing plan fields show on it.

Refused when: see [saving](../drafts.md#save).

API: `POST documents`.

### Make one invoice by hand

Steps:
1. Open the contract and use **Generate**, then the invoice type.

Result: a draft invoice with the contract's lines, dated today. If the contract has a billing plan, its next date moves on by its months, in the same transaction, as it does in the [invoice run](../../contracts/invoice-run.md), so the period is not billed twice. A renewal does not move it. On a contract type billed by a plan, Generate does not offer the contract's own type: use **Renew** under [Renewals](../../contracts/renewals.md).

Refused when: "it ended on {date}." See [flows](../flows.md).

API: `POST documents/{id}/make/{type}`.

### Set a billing plan

See [billing plan](../../contracts/billing-plan.md).

## Related

- [The Contracts menu](../../contracts/README.md)
- [Offers](offers.md)
- [Flows](../flows.md)

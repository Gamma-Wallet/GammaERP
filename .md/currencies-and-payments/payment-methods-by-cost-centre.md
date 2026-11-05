# Payment methods by cost centre

Links a payment method to a cost centre: the cost centre offers only its linked methods, and the link can say where that method's money goes there.

**Where:** Currencies and payments › Payment methods by cost centre (`/config/payment-method-cost-centers`, table `tbl_inter_payment_method_cost_center`). **Who:** an administrator changes; see [configuration-lists.md](../accounting/configuration-lists.md).

## Columns

| Column | Meaning |
|--------|---------|
| Payment method | The method. |
| Cost center | The cost centre. |
| Cash bank | The cash desk or bank this method's money goes to at this cost centre (card payments at a shop to the bank account, say). Empty: where the point of sale's settings send it. Column `cash_bank_id`. |

## Actions

### Link a method to a cost centre

Steps:
1. Press **Add**.
2. Choose the method, the cost centre and, if its money goes elsewhere, the **Cash bank**. Press **Save**.

Result: from then on the cost centre offers only its linked methods: at the point of sale, and in the document editor when the document's money side (To, else From) is that cost centre. A cost centre with no link offers every method. At the point of sale, a payment by this method goes to the link's cash desk or bank (`GET /api/v1/pos/payment-methods?costCenter=`, `GET /api/v1/documents/payment-methods?costCenter=`).

Refused when: see [configuration-lists.md](../accounting/configuration-lists.md).

API: `POST /config/payment-method-cost-centers`.

### Switch a link off

Steps: open it, press **Switch off…** and confirm.

API: `DELETE /config/payment-method-cost-centers/{id}`.

## Open points

- Not tried with data yet: a card payment at a point of sale going to the linked bank needs a company made after 2026-11-04 (the point-of-sale receipt of money and refund got a bank-account line in the packs then).

## Related

- [payment-methods.md](payment-methods.md)

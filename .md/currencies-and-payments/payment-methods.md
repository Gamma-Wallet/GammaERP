# Payment methods

The ways a document can be paid. The starter pack has Cash, Unpaid and Credit card.

**Where:** Currencies and payments › Payment methods (`/config/payment-methods`, table `tbl_payment_method`). **Who:** an administrator changes; see [configuration-lists.md](../accounting/configuration-lists.md).

## Columns

| Column | Meaning |
|--------|---------|
| Code, Name, Description | What people read. |
| Is cash | What it takes goes in the drawer and is counted when a shift closes. |

### Is cash

When a point-of-sale shift closes, the expected cash is the opening cash, plus the payments of methods with **Is cash** ticked, plus money in, less money out. Payments of other methods are not counted in the drawer. A payment with no method counts as cash.

In the starter pack only Cash has it ticked.

## Which methods a document offers

On a document type's **Payment methods** tab (`payment-method-document-types`), under System settings › Document types. A type naming none offers every method. A document with a method its type does not name is refused: `NotConfiguredError`: "the document type does not take this payment method."

## Actions

### Add or change a method

Steps:
1. Press **Add**, or click a method.
2. Fill in the columns, tick **Is cash** if it fits, and press **Save**.

Result: documents can use it. Shifts closed from now on count it by the new setting.

Refused when: see [configuration-lists.md](../accounting/configuration-lists.md).

API: `POST /config/payment-methods`.

### Switch a method off

Steps: open it, press **Switch off…** and confirm.

Result: it is no longer offered. Nothing is deleted.

API: `DELETE /config/payment-methods/{id}`.

## Related

- [payment-methods-by-cost-centre.md](payment-methods-by-cost-centre.md)
- [../accounting/configuration-lists.md](../accounting/configuration-lists.md)

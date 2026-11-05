# Document type transactions

What lines a document type holds, how they are filled and how they post, plus its VAT groups and payment methods.

**Where:** System settings › Document types › a type › Transaction types, VAT groups, Payment methods. **Who:** an administrator changes rows. The tables work as in [Configuration screens](configuration-screens.md).

## Transaction types tab

### Give the type a transaction type

Configuration: `tbl_inter_document_type_transaction_type`, model `document-type-transaction-types`.

| Field | Column | Meaning |
|---|---|---|
| Transaction type | `TransactionTypeId` | A kind of line this document type holds. |
| Element debit value elements | `ElementDebitValueElementsId` | On a journal: the amount that goes on the debit of the line's own balance. Empty: none. |
| Element credit value elements | `ElementCreditValueElementsId` | The same, on the credit. |

Steps:

1. Open **Transaction types** and press **Add** in the first table.
2. Choose the Transaction type.
3. Press **Save**.

Result: the document editor offers this kind of line. Fills, sides and scenarios hang on this row.

Refused when: as in [Configuration screens](configuration-screens.md).

API: `GET /config/document-type-transaction-types?documentTypeId=`, `POST /config/document-type-transaction-types`, `DELETE /config/document-type-transaction-types/{id}`

The Migration step Transaction types of document types (`LINE_KINDS`) does the same from a file.

### Set how its lines are filled

Choose it under **Transaction type** on the tab. The tables below it are for that kind of line.

Configuration: `tbl_transaction_fill`, model `transaction-fills`.

| Fields | Choices |
|---|---|
| Transaction type, VAT group, Element, Unit, Location, Unit price without VAT, Discount, Quantity, Price without VAT, VAT | Typed; Chosen from the list; Not needed |
| Default quantity | The quantity a line gets when none is given. |
| Default discount | The discount per unit a line gets when none is given. |

Steps:

1. Under **How its lines are filled** press **Add**, or press the row.
2. Choose per field. Press **Save**.

Result: the document editor shows the fields this way, and a save checks them.

API: `GET /config/transaction-fills?documentTypeTransactionTypeId=`, `POST /config/transaction-fills`

### Set its lines' from / to

Configuration: `tbl_transaction_type_conf_from`, `tbl_transaction_type_conf_to`. Models `transaction-type-from`, `transaction-type-to`.

Under **Its lines' from / to** there are two tables, From first. Each row has Elements, a scope of `tbl_elements`, for the side of this kind of line.

Steps:

1. Press **Add** in the table.
2. Choose the Elements. Press **Save**.

API: `GET /config/transaction-type-from?documentTypeTransactionTypeId=`, `POST /config/transaction-type-from`. The same for `transaction-type-to`.

### Read its posting scenarios

Under **Posting scenarios** are this kind of line's scenarios (model `scenarios`). The engine picks the most specific scenario that fits a line. No scenario fitting refuses the posting (`NoScenarioError`).

They are managed as in [Posting scenarios](../accounting/posting-scenarios.md).

API: `GET /config/scenarios?documentTypeTransactionTypeId=`

## VAT groups tab

The VAT groups a line of this type may take.

Configuration: `tbl_inter_document_type_vat_group`, model `document-type-vat-groups`. One field: VAT group.

Steps:

1. Open **VAT groups** and press **Add**.
2. Choose the VAT group. Press **Save**.

Result: a line of this type may take it.

API: `GET /config/document-type-vat-groups?documentTypeId=`, `POST /config/document-type-vat-groups`, `DELETE /config/document-type-vat-groups/{id}`

The Migration step VAT groups of document types (`DOCUMENT_VAT_GROUPS`) does the same from a file.

## Payment methods tab

The payment methods of this type.

Configuration: `tbl_inter_payment_method_document_type`, model `payment-method-document-types`. One field: Payment method.

Steps:

1. Open **Payment methods** and press **Add**.
2. Choose the Payment method. Press **Save**.

API: `GET /config/payment-method-document-types?documentTypeId=`, `POST /config/payment-method-document-types`, `DELETE /config/payment-method-document-types/{id}`

## Related

- [Document types](document-types.md)
- [Document type sides](document-type-sides.md)
- [Transaction types](transaction-types.md)
- [Transaction scopes](transaction-scopes.md)
- [Document flows](document-flows.md)
- [Payment methods](../currencies-and-payments/payment-methods.md)
- [VAT groups](../accounting/vat-groups.md)

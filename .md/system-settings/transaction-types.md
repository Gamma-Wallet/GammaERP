# Transaction types

The kinds of line a document can hold, also called line types. A transaction has categories and types only: its rows are the lines of documents.

**Where:** System settings › Transaction types. **Who:** an administrator, or a user with Write on a category of `tbl_transaction`. Saving categories and types, and copying properties, also needs System settings › Transaction types in your menu, checked on the server.

The screen opens directly on the set-up view of the register. It works as in [Types and categories](types-and-categories.md).

From the starter pack:

| | Rows |
|---|---|
| Categories | Transactions, Journal lines, Depreciation lines, Revaluation lines |
| Types | Account, Item, Fixed asset, Service item, Supplier, Client, Cash desk account, Bank account, Staff, Depreciation, Revaluation, Fixed asset (service), Combined item, Customs, Transport, Customs agency, Customs, assets; Transport, assets; Customs agency, assets; Raw material used, Produced item, Waste |

Codes matter: a sale from the group finds a line's transaction type in the company's books by its code.

## Actions

### Add a transaction type

Steps:

1. Under Types press **Type**.
2. Fill in the Name and Code, and choose its Categories.
3. Press **Save**.

Result: the type exists. To use it:

1. Give it a scope in [Transaction scopes](transaction-scopes.md).
2. Give it to a document type in [Document type transactions](document-type-transactions.md).
3. Add its posting scenario.

Refused when: as in [Types and categories](types-and-categories.md).

API: `GET /transactions/categories`, `POST /transactions/categories`, `GET /transactions/types`, `POST /transactions/types`, `POST /transactions/types/{id}/properties/copy`

### Bring types in from a file

The Migration steps Transaction categories (`LINE_CATEGORIES`) and Transaction types (`LINE_TYPES`). See [Migration](migration.md).

## Related

- [Transaction scopes](transaction-scopes.md)
- [Document type transactions](document-type-transactions.md)
- [Types and categories](types-and-categories.md)

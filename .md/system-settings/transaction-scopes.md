# Transaction scopes

What a line of a transaction type picks from, and whether it moves stock. Also called line scopes. A transaction type can have several, and the document editor offers them.

**Where:** System settings › Transaction scopes. **Who:** anyone with it in their menu reads. An administrator changes rows. The screen's generic actions are in [Configuration screens](configuration-screens.md).

Configuration: `tbl_transaction_type_conf`, model `transaction-type-scopes`. The base pack has 21 rows.

| Field | Column | Meaning |
|---|---|---|
| Transaction type | `TransactionTypeId` | The kind of line. |
| Elements | `ElementsId` | The scope a line picks from: a row of `tbl_elements`, a table with maybe a category and a type. |
| Stock direction | `StockDirection` | How the line moves stock. |

Stock direction, as the engine reads it:

| Value | Meaning | In the pack |
|---|---|---|
| 0 | No stock. | Accounts, services, partners. |
| 1 | Out of the From side and into the To side. | Item, Fixed asset. |
| 2 | Out of the From side only. | Raw material used, Product taken apart. |
| 3 | Into the To side only. | Produced item, Recovered component. |
| 4 | Out of the From side only, not part of what is made. | Waste. |
| 5 | Value only: moves no stock, carries its share of the cost. | Disassembly waste. |

On a reversal, a one-sided line moves the other way.

Posting also reads the scope, to find the scenario that fits a line.

## Actions

### Add or change a scope

Steps:

1. Press **Add**, or press a row.
2. Choose the Transaction type and the Elements.
3. Choose the Stock direction: No stock (0); Moves stock, from down, to up (1); Out of the from side only (2); Into the to side only (3); Waste, out of the from side, an expense (4); or Value only, no stock (5).
4. Press **Save**.

Result: the document editor offers this scope on lines of that type.

Refused when: as in [Configuration screens](configuration-screens.md).

API: `GET /config/transaction-type-scopes`, `POST /config/transaction-type-scopes`, `DELETE /config/transaction-type-scopes/{id}`

## Related

- [Transaction types](transaction-types.md)
- [Document type transactions](document-type-transactions.md)
- [Configuration screens](configuration-screens.md)


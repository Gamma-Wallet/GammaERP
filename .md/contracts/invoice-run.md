# Invoice run

Makes the draft invoices of the contracts due up to a day. A person presses the run; nothing runs by itself.

**Where:** Contracts › Invoice run. **Who:** the Invoice run screen in your menu. Each invoice also needs its type given to you and write on its category.

## Actions

### See what is due

Steps:
1. Open the screen. **Due up to** is today.
2. Change the date to look further on.

Result: the contracts with an active plan whose next date is on or before that day, and not after the contract's End date, the earliest first: Contract, Client or supplier, Next date, End date, Every (months), Total. "No contract is due." when empty.

API: `GET contract-billing/due?until=2026-11-30`.

### Make the invoices

Steps:
1. Press **Make the invoices**. It bills every contract in the list. A contract whose next date is after its End date is not due: it is not listed and not billed.
2. Read the result above the list: "{invoice} made from {contract}", and each refused contract with its reason.
3. Check each invoice under Documents and post it.

Result, per contract, both or neither:
- A **draft** invoice through the contract's flow: its lines, sides and payment method copied, dated today, linked to the contract.
- When the contract type has flows to several other types, the run takes the flow to a type that holds an open item (an invoice) first, then by the types' sort order and code. A flow back to the contract's own type is never taken. The base pack has one flow per contract type.
- The whole contract is billed: no days are counted, and the amount is the contract's full lines.
- The plan's next date moves on by its months, in the same transaction as the invoice, so a period is never billed twice. This is part of making an invoice from the contract, so **Generate** on the contract moves it the same way.

Nothing posts here: stock, accounts and balances move only when each invoice is posted.

One refused contract does not stop the others. Its next date stays, so it comes back on the next run.

Refused for a contract when:

| Reason | Meaning |
|--------|---------|
| `NotConfiguredError`: this contract type makes no invoice. | No flow to another type under System settings › Document flows |
| `NotAllowedError`: it ended on {date}. | The contract's End date has passed. Stop the plan, or renew it |
| `NotAllowedError`: that document type has no {kind} rows. | The invoice type does not take a kind of line on the contract |
| `NotAllowedError`: this document type has not been given to you… | The invoice type is not given to you |
| You may not write elements of this category. | No write right on the invoice's category |
| `StockError` | With negative stock forbidden, the invoice's lines exceed stock: see [stock checks](../documents/lines.md#stock-and-cost-checks) |

Refused for the whole screen: `NotAllowedError: this screen is not in your menu.`

API: `POST contract-billing/run?until=2026-11-30` gives `{ made, refused }`.

## Related

- [Billing plan](billing-plan.md)
- [Renewals](renewals.md)
- [Flows](../documents/flows.md)

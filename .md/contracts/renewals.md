# Renewals

The contracts that have ended or end soon, each renewed by hand into a new contract linked to the old one.

**Where:** Contracts › Renewals. **Who:** the Renewals screen in your menu. A renewal also needs the contract type given to you and write on its category.

## Actions

### See what is ending

Steps:
1. Open the screen. **Ending within (days)** is 60.
2. Change it to look further on.

Result: contracts of types that bill by plan, with an end date within that many days, or already past, and not yet renewed. A contract counts as renewed only when a contract of the same type made from it has a later End date, which is what **Renew** makes. A plain copy (Generate, Repeat) does not count. The earliest first: Contract, Client or supplier, End date, Days left, Total. Days left is negative for an ended contract. "No contract is ending." when empty.

The end date is the property the contract's invoice flow stops at (`ValidUntilInfoId` on `tbl_document_type_flow`). A contract with no end date is not listed.

API: `GET contract-billing/renewals?days=60`.

### Renew a contract

Steps:
1. Set the **New end date** on the row.
2. Press **Renew**.
3. The new contract opens. Change the prices and anything else, and save.

Result, all or nothing:
- A new contract of the same type: its lines, sides and payment method copied, its properties copied, the end date set to the new one, dated today, linked to the old contract.
- Other dates in the properties, such as the Start date, are copied as they are.
- If the old contract had a plan, the new one gets the same months, with its next date the day after the old contract's end.
- The old contract's plan is switched off, so it stops billing.
- The old contract leaves this list.

Refused when:

| Message | Meaning |
|---------|---------|
| `NotConfiguredError`: this contract type can't be renewed. | No flow from the type to itself under System settings › Document flows |
| The new end date is after the contract's end. | The new date must be later than the old end. The message states the rule |
| There is no such contract. | Not a contract type, or not in your company |
| `NotAllowedError`: a document of this type was already made from it. | It was renewed already |
| `NotAllowedError`: this document type has not been given to you… | |

API: `POST contract-billing/{id}/renew?endDate=2027-12-31`. The answer is the new contract.

On a contract type billed by a plan, **Generate** does not offer the contract's own type: **Renew** is the action for that.

A Repeat of a contract does not stop its renewal: the renewal flow's "once" check, on a contract type billed by a plan, counts only a copy whose End date is later than the contract's (a renewal). A second Renew of the same contract is refused.

## Related

- [Billing plan](billing-plan.md)
- [Invoice run](invoice-run.md)
- [Contract documents](../documents/categories/contracts.md)

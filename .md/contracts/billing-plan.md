# Billing plan on a contract

A plan bills a contract every so many months, from a next date. It only says what is due: the [invoice run](invoice-run.md), or a person with **Generate** on the contract, makes the invoices. Each invoice made from the contract moves the next date on by the plan's months; a renewal does not.

**Where:** Documents › Contracts › a saved contract, the fields under the header. **Who:** the Documents screen in your menu, to read and to set the plan. Administrators pass. The fields show on types that bill by plan, all contract types in the base pack. An administrator sets this per type in System settings › Document types › the type › General, box "A contract billed by a plan".

Stored in `tbl_contract_billing`: one active row per contract.

## Actions

### Set or change a plan

Steps:
1. Save the contract first: the fields show on a saved contract only.
2. Type **Every (months)**, 1 to 60.
3. Set the **Next date**.
4. Press **Save the billing plan**, or the contract's own **Save**: it saves the plan too when the plan was changed.

Result: the contract shows on the invoice run once its next date is reached. Changing a plan replaces both values. When the contract's Save stores the plan, the contract is saved first; a refused plan shows its message under the plan fields, and the contract stays saved.

Refused when:
- "A plan bills every 1 to 60 months, from a next date." The months are out of range, or the date is empty.
- "There is no such contract." Not a contract type, or not in your company.
- `NotAllowedError`: this screen is not in your menu. The Documents screen is not in your menu.

API: `GET contract-billing/{id}`, `PUT contract-billing/{id}` with `{ "everyMonths": 1, "nextDate": "2026-12-01" }`.

### Stop billing

Steps:
1. Empty **Every (months)**.
2. Press **Save the billing plan**, or the contract's **Save**.

Result: the plan is switched off and the contract leaves the invoice run. Earlier invoices stay. A plan can be set again later.

API: `PUT contract-billing/{id}` with `{ "everyMonths": null }`.

## Related

- [Invoice run](invoice-run.md)
- [Renewals](renewals.md)
- [Contract documents](../documents/categories/contracts.md)

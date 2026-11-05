# Contracts

The contracts of the chosen categories in a period: what each is worth, what was invoiced from it and what remains.

**Where:** Reports › Stock and sales › Contracts. **Who:** the `contracts` report in your menu.

## Actions

### Show the contracts

Filters: **Document categories** (required, one or more), **Partner**, **From**, **Up to**.

Steps:
1. Choose the Contracts category, set the other filters and press **Show**.

Shows:
- Figures: Contracts, Value, Invoiced, Remaining.
- Table: Date, Document, Type, Client or supplier, Ends, Value, Invoiced, Remaining.

Posting a contract books nothing, so drafts and posted contracts both count. It is dated by its posting date, else its document date. Value is its total with VAT. Invoiced is the posted documents made from it, less their reversals. Ends is the value of the property its document flow names (`valid_until_info_id` on Document flows).

Result: nothing changes. Clicking a row opens the contract.

Refused when:
- "Name at least one document category."
- "The period ends before it starts."

API: `GET /reports/contracts?category=&partner=&from=&to=`.

## Related

- [document-totals.md](document-totals.md)
- [client-statistics.md](client-statistics.md)

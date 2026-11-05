# Posting scenarios

A scenario says how one kind of document line posts to the accounts. It belongs to a document type × transaction type, with a scope that says which lines it fits. Its lines are on [scenario-lines.md](scenario-lines.md).

**Where:** Accounting setup › Posting scenarios (`/config/scenarios`, table `tbl_scenario`). **Who:** an administrator changes; see [configuration-lists.md](configuration-lists.md).

## Columns

| Column | Meaning |
|--------|---------|
| Name, Description | What people read. |
| Document type transaction type | The link between a document type and a transaction type (`tbl_inter_document_type_transaction_type`). |
| Elements | The scope: an `tbl_elements` row naming a table and, if set, a category, a type or one record. An empty part fits all. |

## How posting picks a scenario

For each line of a document being posted:

1. The engine takes the scenarios of the document type × the line's transaction type, of the company, its owner node, or shared.
2. It keeps those whose scope fits the line.
3. The most specific one wins.

All or nothing: every entry is worked out and checked before any is written.

## What refuses a posting

- `NoScenarioError`: "no scenario fits the transaction ...". Add a scenario for this line.
- `NotConfiguredError`: "the transaction's scenario has no lines", or a line has no account or no value that names an amount.
- `UnbalancedError`: "debit ... ≠ credit ...".
- `GroupAccountError`: an entry would go to a group account.

## Actions

### Add or change a scenario

Steps:
1. Press **Add**, or click a scenario.
2. Choose the document type × transaction type and the scope. Name it.
3. Press **Save**, then add its lines under Scenario lines.
4. Open Unbalanced scenarios to check it.

Result: documents posted from now on use it. Entries already posted do not change.

Refused when: see [configuration-lists.md](configuration-lists.md).

API: `POST /config/scenarios`.

### Switch a scenario off

Steps: open the scenario, press **Switch off…** and confirm.

Result: posting stops using it. A line it served then needs another scenario, or the posting is refused.

API: `DELETE /config/scenarios/{id}`.

### Export, template and import

Migration step `POSTING_GROUPS`. See [configuration-lists.md](configuration-lists.md).

## Related

- [scenario-lines.md](scenario-lines.md)
- [unbalanced-scenarios.md](unbalanced-scenarios.md)
- [chart-of-accounts.md](chart-of-accounts.md)

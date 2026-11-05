# Depreciation scenarios and lines

A depreciation scenario does two things for the assets it fits in a book: it proposes their life and percentages at registration, and its lines give the accounts of the journal.

**Where:** Fixed assets › Depreciation scenarios (`/config/depreciation-scenarios`, table `tbl_depreciation_scenario`) and Fixed assets › Depreciation scenario lines (`/config/depreciation-scenario-lines`, table `tbl_depreciation_scenario_conf`). **Who:** an administrator changes; see [configuration-lists.md](../accounting/configuration-lists.md).

## Scenario columns

| Column | Meaning |
|--------|---------|
| Name, Description | What people read. |
| Depreciation book | The book it belongs to. |
| Elements | The scope: on the product table, with a category, a type or one product if set. |
| Months | The life registering proposes. Empty: none, so an asset needs months typed in. |
| Linear percentage | The yearly percentage it proposes. |
| Declining percentage | The yearly percentage it proposes. |

## Line columns

| Column | Meaning |
|--------|---------|
| Depreciation scenario | The scenario it belongs to. |
| Chart | The account. |
| Chart name | Text kept beside the account. |
| Side | Ticked: credit. Not ticked: debit. |
| Side value | The side as text. |

Each line takes the run's whole amount. A balanced scenario has as many debit lines as credit lines, each on an account. Accounting setup › [Unbalanced scenarios](../accounting/unbalanced-scenarios.md) lists the active depreciation scenarios that have no lines, a line with no account, or unequal debit and credit lines. A journal with unbalanced lines is refused when posted.

## Which scenario fits an asset

Among the book's active scenarios on the product table, whose category, type and record fit the product:

1. one on the product itself
2. then one on its type
3. then one on a category, its own before the one above it

## Actions

### Add or change a scenario

Steps:
1. Press **Add**, or click a scenario.
2. Choose the book and scope. Enter months and percentages. Press **Save**.

Result: registrations from now on see the proposal. Registered assets keep what they have.

Refused when: see [configuration-lists.md](../accounting/configuration-lists.md).

API: `POST /config/depreciation-scenarios`.

### Add or change a line

Steps:
1. Press **Add**, or click a line.
2. Choose the scenario, account and side. Press **Save**.

Result: journals from now on use it.

API: `POST /config/depreciation-scenario-lines`.

### Switch a scenario or line off

Steps: open it, press **Switch off…** and confirm.

Result: a switched-off scenario no longer fits. With none left, the journal is refused with `NoScenarioError`.

API: `DELETE /config/depreciation-scenarios/{id}`, `DELETE /config/depreciation-scenario-lines/{id}`.

## Related

- [registering-an-asset.md](registering-an-asset.md)
- [depreciation-run.md](depreciation-run.md)
- [books.md](books.md)

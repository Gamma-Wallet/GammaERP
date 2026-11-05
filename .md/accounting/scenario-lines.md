# Scenario lines

One line of a posting scenario: which account, debit or credit, which amount of the document line, and where it is written.

**Where:** Accounting setup › Scenario lines (`/config/scenario-lines`, table `tbl_scenario_conf`). **Who:** an administrator changes; see [configuration-lists.md](configuration-lists.md).

## Columns

| Column | Meaning |
|--------|---------|
| Scenario | The scenario it belongs to. |
| Chart | The account, for places 1 and 2. Empty for the others, where the engine finds it. |
| Chart name | Text kept beside the account. |
| Side | Ticked: credit. Not ticked: debit. |
| Side value | The side as text. |
| Value elements | The amount the line takes: a "Transaction amount" row of `tbl_elements`, which names the column of `tbl_transaction` it reads. This is what posting uses. |
| Value, Value name | The number of the amount and its name, kept for reference. |
| Place | Where it is written, see below. |
| Cost center place | The cost centre the entry carries: 1 the from side, 2 the to side, empty for none. |

### Place

| Place | Written |
|-------|---------|
| 1 | On the account named, only when the document's from side has an element. |
| 2 | On the account named, only when the to side has an element. |
| 3 | On the line's element, which is then the account (journal account lines). Chart stays empty. |
| 4 | On the account the from side's element holds, such as a cash desk's or bank's account property. |
| 5 | On the account the to side's element holds. |
| 6 | On the account the line's element holds (a cash desk or bank line). |

The Place and Cost center place fields show numbers on the screen.

## Rules the engine keeps

- A line with no account, or no value that names an amount, refuses the posting (`NotConfiguredError`).
- An amount of 0 writes nothing.
- A side with no element skips lines of places 1, 2, 4 and 5.

## Actions

### Add or change a line

Steps:
1. Press **Add**, or click a line.
2. Choose the scenario, side, value and place. Choose the account for places 1 and 2. Save.
3. Open Unbalanced scenarios to check the scenario still balances.

Result: documents posted from now on use it. Refused when: see [configuration-lists.md](configuration-lists.md).

API: `POST /config/scenario-lines`.

### Switch a line off

Steps: open the line, press **Switch off…** and confirm.

Result: posting stops writing it, which can leave the scenario unbalanced.

API: `DELETE /config/scenario-lines/{id}`.

## Related

- [posting-scenarios.md](posting-scenarios.md)
- [unbalanced-scenarios.md](unbalanced-scenarios.md)
- [../fixed-assets/selling-an-asset.md](../fixed-assets/selling-an-asset.md)

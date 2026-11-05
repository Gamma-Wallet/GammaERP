# Unbalanced scenarios

A check of the company's active posting scenarios and depreciation scenarios: it lists those that can never balance, or post nothing, and why. It changes nothing.

**Where:** Accounting setup › Unbalanced scenarios. **Who:** the `unbalanced-scenarios` report in your menu. Administrators pass.

## Actions

### Run the check

Steps:
1. Open the screen and press **Show**.

Result: one row per scenario that cannot balance: document type, scenario, and why. A depreciation scenario shows "Depreciation: {book}" as its document type. An empty list means every active scenario has lines and balances.

How it works:
- A posting scenario's lines are posted for two unrelated sample lines made by the documents' own line formula. Debit must equal credit on both.
- A posting scenario with no lines is listed too: a document that needs it is refused when posted.
- A depreciation scenario posts the run's amount on each line, so it balances only with as many debit lines as credit lines, each on an account.

Reasons you can see:
- `NotConfiguredError`: the line's value names no amount of the transaction.
- `UnbalancedError`: the lines do not balance, with the sample's debit and credit.
- `NotConfiguredError`: the scenario has no lines; a document that needs it is refused when posted.
- `NotConfiguredError`: the depreciation scenario has no lines.
- `NotConfiguredError`: a line of the depreciation scenario names no account.
- `UnbalancedError`: each line posts the run's amount, so it needs as many debit lines as credit lines.

Refused when: "this screen is not in your menu".

API: `GET /posting/unbalanced-scenarios`.

## Related

- [posting-scenarios.md](posting-scenarios.md)
- [scenario-lines.md](scenario-lines.md)
- [../fixed-assets/depreciation-scenarios.md](../fixed-assets/depreciation-scenarios.md)

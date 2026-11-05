# Depreciation run

Works out a book's depreciation up to a date, records it, and makes its journal. Three separate steps, all by hand.

**Where:** Fixed assets › Depreciation, **Run** and **Past runs** tabs. **Who:** the Depreciation item in your menu. Administrators pass.

## How the amount is worked out

For each asset value of the book that is not sold or disposed:

- The period runs from its last depreciation (or its start) to the date chosen, or its end date if that comes first. A period of no days is skipped.
- The amount is the method's formula. See [methods.md](methods.md).
- It is rounded to 2 decimals and never takes the book value below zero.
- The cost centre is the asset's depreciation cost centre, or else the place that holds it.

## Actions

### Preview a run

Steps:
1. Choose the book. Open the **Run** tab.
2. Set **Up to** and press **Preview**.

Result: nothing is saved. The screen shows depreciation, book value before and after, and a row per asset with its period and days.

Refused when:
- "The book does not exist."
- `NotConfiguredError`: the asset value has no depreciation method, or its formula can't be worked out.

API: `GET /depreciation/books/{book}/calculate?date=`.

### Record the run

Steps:
1. Press **Preview**, then **Run**. The button is off until there are lines, and after the run.

Result: one row per asset with an amount above 0 in `tbl_depreciation`, not yet in a journal. The book values drop. No account changes yet.

An asset whose end date falls before the date chosen gets its row on its end date. It then shows as its own run in Past runs, and its journal needs that date in **Up to**.

Refused when:
- `PeriodClosedError`: {date} is in a closed period (up to {day}). The date is on or before the company's closed-period day; see [closed periods](../accounting/closed-periods.md).
- "Depreciation runs in a company."
- Those of the preview.

API: `POST /depreciation/books/{book}/runs?date=`.

### Make the journal

Steps:
1. Keep **Up to** on the run's date. It can be a run recorded earlier.
2. Choose **Journal type**, such as Depreciation journal, and **In category**.
3. Press **Make the journal**.

Result: a document of that type with, per asset, the lines of the book's depreciation scenario that fits it best, each for the run's amount on its account and side, with the company's own VAT group marked Depreciation (see [VAT groups](../accounting/vat-groups.md)). If the journal type sits under a sheet type, a sheet is made and the journal goes under it. The run's rows are linked to it. The journal opens. If the book posts, the journal and its sheet are posted; otherwise the journal stays a draft.

Saving, linking and posting are one database transaction: if posting is refused, nothing is kept, no draft journal stays, and the run's rows stay out of a journal. Fix what refused it and press **Make the journal** again.

Refused when:
- `PeriodClosedError`: {date} is in a closed period (up to {day}). Checked before anything is made.
- "The book has no depreciation on this date that is not in a journal yet."
- `NotConfiguredError`: the journal's document type has no transaction type on accounts.
- `NoScenarioError`: no depreciation scenario of the book fits the product.
- Anything that refuses a posting, such as `PeriodClosedError`.

API: `POST /depreciation/books/{book}/journals` with `date`, `documentTypeId`, `categoryId`.

### See past runs

Steps:
1. Open the **Past runs** tab. The latest is on top.
2. Click a run to see its assets. Press **Open journal** to open its journal.

Result: nothing changes.

API: `GET /depreciation/books/{book}/runs`, `GET /depreciation/books/{book}/runs/{date}`.

### Undo the latest run

Steps:
1. Open **Past runs** and press **Undo** on the latest run. Only that one has the button.
2. Read what will happen and confirm.

Result: the assets go back to their book value before the run. The run's rows are switched off, and:
- with a posted journal, the journal is reversed on the run's date
- with an unposted journal, the journal is removed
- with no journal, only the run goes

Refused when:
- "Only the latest run of a book can be undone."
- Anything that refuses the reversal, such as `PeriodClosedError` when the run's date is closed.

API: `DELETE /depreciation/books/{book}/runs/{date}`.

### Reverse the journal from Documents

Reversing a depreciation journal the usual way cancels its run too.

Refused when: `ReversalError`: "a later depreciation run stands on this one; undo the later run first."

## Related

- [methods.md](methods.md)
- [depreciation-scenarios.md](depreciation-scenarios.md)
- [books.md](books.md)
- [../accounting/closed-periods.md](../accounting/closed-periods.md)

# Closed periods

The company's lock on posting: nothing posts on a day up to the locked date, reversals included.

**Where:** Accounting setup › Closed periods (`/config/closed-periods`, table `tbl_posting_lock`). **Who:** an administrator changes; see [configuration-lists.md](configuration-lists.md).

## Columns

| Column | Meaning |
|--------|---------|
| Locked until | The last closed day. |

If the company has more than one active row, the latest date counts.

## Actions

### Lock up to a day

Steps:
1. Press **Add**, or click the row.
2. Set **Locked until** and press **Save**.

Result: posting on that day or earlier is refused with `PeriodClosedError`: "the period is closed up to ...". There is no check of drafts or balances here; [year-closing.md](year-closing.md) does those.

API: `POST /config/closed-periods`.

### Move the lock back

Steps:
1. Click the row, set an earlier date and press **Save**. Or switch the row off to remove the lock.

Result: days after the new date can be posted again. A year whose last day is after the lock shows as not closed on Close the year.

API: `POST /config/closed-periods`, `DELETE /config/closed-periods/{id}`.

## What the lock stops

- Posting any document, including a reversal, whose posting date is in the closed period.
- Undoing a depreciation run with a posted journal, if the run's date is closed: the undo posts a reversal on that date.
- Recording a depreciation run, or making its journal, on a day in the closed period.
- Posting a depreciation journal in a closed period.

## Related

- [year-closing.md](year-closing.md)
- [../fixed-assets/depreciation-run.md](../fixed-assets/depreciation-run.md)

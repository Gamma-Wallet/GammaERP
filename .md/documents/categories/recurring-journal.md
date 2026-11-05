# Recurring journal

Sheets meant to be done again each period. They work as [Journal](journal.md) sheets. A person repeats them: nothing recurs by itself.

**Where:** Documents › Recurring journal. **Who:** see [who may do what](../life-cycle.md#who-may-do-what).

## Types in the base pack

| Sheet | Code | Row types | Reversed by |
|-------|------|-----------|-------------|
| Recurring journal sheet | `RECURRING_JOURNAL_SHEET` | General journal entry, Payment, Receipt, Prepayment, Advance, Employee advance | Reversal of journal sheet |
| Recurring depreciation sheet | `RECURRING_DEPRECIATION_SHEET` | Depreciation journal | Reversal of depreciation sheet |
| Recurring revaluation sheet | `RECURRING_REVALUATION_SHEET` | Revaluation journal | None |

Each row type is a twin of the Journal's: the same sides, kinds of line, fields, VAT groups and scenarios. The sheet above it says it recurs.

## Actions

### Write the sheet once

Steps:
1. Write and post it as a [journal sheet](journal.md#write-a-sheet), or save it and leave it as a draft to copy.

### Do it again for a period

Steps:
1. Open the sheet from the list.
2. Press **Repeat**.
3. Set the date. Tick **Post it straight away** if the amounts stand as they are.
4. Press **Repeat**.

Result: a copy of the sheet and its rows for that day, linked to the one it repeats. It opens on the screen. When it is not posted, the person changes the amounts and posts it.

Refused when: every rule of a new document and of posting. Without the tick the copy may still fail at Post, for example on a closed period.

API: `POST documents/{id}/repeat` with `{ "date": "2026-11-02", "post": true }`.

## Related

- [Journal](journal.md)
- [Flows and repeat](../flows.md#repeat-a-document)

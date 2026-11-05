# Journal

Sheets of journal rows. A sheet is a document with no lines of its own: each row of its one grid is a journal document under it, with one line. Debit = credit holds over the whole sheet.

**Where:** Documents › Journal. **Who:** see [who may do what](../life-cycle.md#who-may-do-what). The user needs the sheet's type and its journal types given.

## Types in the base pack

| Sheet | Code | Row types | Reversed by |
|-------|------|-----------|-------------|
| Journal sheet | `JOURNAL_SHEET` | General journal entry, Payment, Receipt, Prepayment, Advance, Employee advance | Reversal of journal sheet |
| Depreciation sheet | `DEPRECIATION_SHEET` | Depreciation journal | Reversal of depreciation sheet |
| Revaluation sheet | `REVALUATION_SHEET` | Revaluation journal | None |

The rows of a depreciation sheet normally come from Fixed assets › Depreciation.

## Actions

### Write a sheet

Steps:
1. Press **New** and pick the sheet.
2. Set the dates in the header.
3. Press **Add row**. A new row repeats the last row's type and place.
4. Pick **Document type**, **Kind**, **What** and **Where**. Where only one place can be chosen, it is filled in.
5. Type **Debit** or **Credit**. Typing one clears the other. Add a **Description**.
6. Watch **Debit**, **Credit** and **Difference** below the grid. A difference shows in red.
7. Save.

Result: the sheet and each row are saved together, all or nothing. Each row is a journal with one line of quantity 1, dated by the sheet, with a VAT group of 0 %. Rows taken out are switched off. A sheet may be saved out of balance.

Refused when:
- The sheet has row types, but none is given to you: the screen says "This sheet has row types, but none is given to you: ask an administrator for them." and shows an empty line grid. An administrator gives the row types on the Users tab of System settings › Document types.
- `NotConfiguredError`: this document type is not one of the sheet's.
- A journal has exactly one transaction. (API only.)
- A row's refusal, as for any [line](../lines.md#refused-on-a-line), named by its row.

API: `POST documents/sheet` with `{ sheet, journals }`. `GET documents/{id}/journals` reads the rows.

### Post a sheet

Steps:
1. Look at the Posting tab: it shows the entries of all rows.
2. Press **Post**.

Result: the sheet posts with its unposted journals, all or nothing. Each row moves the balances of what it names, for example a client on a Client line.

Refused when:
- `UnbalancedError`: debit x ≠ credit y. Checked over the sheet, not per row.
- The sheet has no rows to post.
- Any refusal of [posting](../posting.md#post).

API: `POST documents/{id}/post`.

### Reverse a sheet

Steps:
1. See [reversing a sheet](../reversing.md#reverse-a-sheet). A sheet is reversed only whole.

### Repeat a sheet

Steps:
1. See [repeat](../flows.md#repeat-a-document).

The Payment and Receipt row types of the journal sheets (and of the recurring sheets) cover invoices as the Payment and Receipt documents do: their lines name the invoices they pay. The Revaluation journal's lines take an account.

## Related

- [Recurring journal](recurring-journal.md)
- [Reversals](reversals.md)
- [Posting](../posting.md)

# Bank statement formats

How one bank writes its statement file. A file is read with the format you choose when you upload it. The starter pack has one: "Date, text, reference, amount".

**Where:** Currencies and payments › Bank statement formats (`/config/bank-statement-formats`, table `tbl_bank_statement_format`). **Who:** an administrator changes; see [configuration-lists.md](../accounting/configuration-lists.md).

## Columns

Columns count from 1. A statement has either one amount column, or one column for money in and one for money out.

| Column | Meaning |
|--------|---------|
| Name | What people choose at upload. |
| Separator | What separates the columns. |
| Header lines | How many lines at the top are not statement lines. |
| Date column | The column of the date. |
| Date format | How the file writes a date, such as `dd/MM/yyyy`. |
| Text column | The column of the text. |
| Reference column | The column of the reference, if any. |
| Amount column | Money in positive, money out negative. |
| Money in column, Money out column | Used when there is no amount column. Money out may be written negative. |
| Decimal comma | Ticked when the file writes `1.234,56` and not `1,234.56`. |

## Actions

### Add or change a format

Steps:
1. Press **Add**, or click a format.
2. Fill in the columns from a sample of the bank's file. Press **Save**.
3. Upload a file with it under Documents › Bank statements to test it.

Result: the format is offered at upload, for the company that owns it. Statements already uploaded do not change.

Refused when: see [configuration-lists.md](../accounting/configuration-lists.md). Saving does not test the columns.

At upload a wrong format shows as "Line ...: the date can't be read." or "Line ...: the amount can't be read."

API: `POST /config/bank-statement-formats`.

### Switch a format off

Steps: open it, press **Switch off…** and confirm.

Result: it is no longer offered at upload.

API: `DELETE /config/bank-statement-formats/{id}`.

## Related

- [payment-methods.md](payment-methods.md)

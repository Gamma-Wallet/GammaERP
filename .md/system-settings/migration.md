# Migration

Bringing a company's lists in from Excel files, in the order they depend on each other. Each step exports what is here, gives an empty template, and imports a file. An import is checked first and saved only on Confirm: every row, or none.

**Where:** System settings › Migration. **Who:** the `migration` screen in your menu, on the server too.

The steps, their order and what each needs are configuration. See [Migration steps](migration-steps.md).

## How to read the screen

Each step sits under what it needs and opens once everything above it holds data.

| Column | Meaning |
|---|---|
| Step | Its number and name. |
| Needs | The steps it names. |
| Rows | What it holds now. |
| State | Waiting, Ready or Imported. |
| Last import | When, who, how many added and updated. |

When a step balances on an opening account, a line on top says where that account stands. It must end at zero.

## Actions

### Export what is here

Steps:

1. Press **Export** on the step.

Result: `<code>.xlsx`, in the template's columns, so a corrected export can be imported back. Nothing changes.

Refused when: `NotAllowedError: Migration is not in your menu.` (403), or "There is no such step."

API: `GET /migration/{code}/export`

### Get the template

Steps:

1. Press **Template** on the step.

Result: `<code>-template.xlsx`. The sheet **Data** has the columns. The sheet **Allowed values** says for each column whether it is required and what it takes, with the codes this company has.

API: `GET /migration/{code}/template`

### Import a file

Steps:

1. Fill in the sheet Data.
2. Press **Import** on the step. It stays off while the step is Waiting.
3. Choose the `.xlsx` file. It is checked at once and nothing is saved.
4. Read the answer: how many rows to add and to update, or the problems by Row, Column and Problem.
5. With problems, fix the file and start again. Without problems, press **Confirm**, or **Cancel**.

Result:

- A check and an import are one path: the rows go through the same saves as the screens, inside a transaction, and a check rolls it back. So a check finds what an import would.
- Confirm saves every row. With any error it saves none.
- Rows are matched by code: an existing row is updated, a new one is added. Each step's template says how.
- The run is kept in `tbl_migration_run`, and the step shows as Imported.
- An opening step posts a journal sheet, kept with the run. The answer says where the opening account stands after this file.

Refused when:

- `First fill in: <steps>.`
- "The file is too large." The limit is 10 MB.
- "The file is not an Excel workbook (.xlsx)."
- "The sheet is empty."
- "This column is not in the template."
- "This column appears twice."
- "This required column is missing."
- `A file holds at most 20000 rows.`
- "A formula is not read: paste the value."
- A row breaks a rule of its list: the row's message.

API: `POST /migration/{code}/import?confirm=false` to check, `confirm=true` to save, multipart, field `file`. `GET /migration` gives the steps, `GET /migration/opening` the opening account.

## Importing from a list's own screen

The same Export, Template and Import show on these screens, for those with Migration in their menu:

| Screen | Step |
|---|---|
| Accounting setup › Chart of accounts | `CHART` |
| Accounting setup › Posting scenarios | `POSTING_GROUPS` |
| Accounting setup › VAT groups | `VAT_GROUPS` |
| Accounting setup › VAT return boxes | `VAT_RETURN_BOXES` |
| Accounting setup › VAT return box rules | `VAT_RETURN_BOX_RULES` |
| System settings › Document types | `DOCUMENT_CATEGORIES`, `DOCUMENT_TYPES` |

## Related

- [Migration steps](migration-steps.md)
- [Document types](document-types.md)
- [Create a company](../getting-started/create-a-company.md)

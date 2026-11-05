# Configuration lists

Most setup screens are the same screen: a list of rows from one configuration table of the company. The engine reads these rows; nothing about them is in the code. This page holds what they all share, so the other pages only give their columns and rules.

**Where:** any menu item that opens a configuration model, such as Accounting setup › VAT groups. **Who:** an administrator changes rows. Anyone else who has the item in their menu reads them, and a row opens read-only.

## Actions

### Find a row

Steps:
1. Type in **Search**. The list keeps the rows with the text in any shown cell.

Result: nothing changes. The list shows the code and name first, then the other columns, seven at most, as a tree when rows sit under other rows (**Under** on the form). A row's form shows all of the columns. The row count stands beside the search box.

API: `GET /config/{model}`, with any column as a filter in the query string. `GET /config/{model}/{id}` reads one row. `GET /config/{model}/schema` gives the columns. `GET /config/{model}/choices/{column}?search=` gives what a link column may point at, 200 at most.

### Add a row

Steps:
1. Press **Add** (administrators only).
2. Fill in the form. Required fields are marked. A translated field takes a text per language. A link field is a searchable list.
3. Press **Save**.

Result: a new row of the company you are working in. The engine reads it from the next action on.

Refused when:
- "only an administrator changes the configuration": you are not an administrator.
- "Configuration belongs to a company: choose one of its nodes": you are not inside a company.
- "there is no such ... for this company": a link points at a row the company does not see.
- "{model} has no column {name}" or "is not a valid ...": the API call sent a wrong column or value.
- "the database refuses it (...)": a database rule said no, such as a link that does not exist.

API: `POST /config/{model}` with the row as JSON, no `id`.

### Change a row

Steps:
1. Click the row.
2. Change the fields and press **Save**.

Result: the columns sent change, the others stay. The old version stays as history. Refused for the same reasons as adding.

API: `POST /config/{model}` with the row's `id`.

### Switch a row off

Steps:
1. Click the row.
2. Press **Switch off…** and confirm.

Result: the engine stops reading the row. Nothing is deleted and its history stays.

Refused when: you are not an administrator.

API: `DELETE /config/{model}/{id}`.

### Export, template and import

These buttons show only on lists that are also a Migration step, and only if you have Migration in your menu: Posting scenarios, VAT groups, VAT return boxes, VAT return box rules. The Chart of accounts has them too.

Steps:
1. **Export** downloads the rows as Excel. **Template** downloads an empty sheet.
2. **Import**, then choose an `.xlsx` file. The file is checked first.
3. If there are problems, the screen lists them and nothing is saved. If not, it says how many rows it would add and update.
4. Press **Confirm** to save, or **Cancel**.

Result: every row is saved, or none.

Refused when: "Migration is not in your menu".

API: `GET /migration/{code}/export`, `GET /migration/{code}/template`, `POST /migration/{code}/import` (without `confirm=true` it only checks).

## Related

- [vat-groups.md](vat-groups.md)
- [posting-scenarios.md](posting-scenarios.md)
- [../fixed-assets/books.md](../fixed-assets/books.md)
- [../currencies-and-payments/payment-methods.md](../currencies-and-payments/payment-methods.md)

# Configuration screens

Every configuration list works the same way: a table of rows, and a form built from the table's own columns. Everything the engine reads is a row like this.

**Where:** any menu item with the screen `config`, and the tabs of Document types. **Who:** any signed-in user of the company can read them; only the menu rows are for administrators. Only an administrator adds and changes rows. Others see **Close** instead of **Save**.

`{model}` below is the list's name in the API, such as `approval-limits`. `GET /config` gives them all.

## Actions

### Read a list

Steps:

1. Open the menu item.
2. Type in **Search** to filter what is loaded.

Result: at most seven columns, code and name first, 100 rows a page. A row with a parent shows under it in the tree. Links show the name of what they point at.

API: `GET /config/{model}`, with a column as a query parameter to filter by it. `GET /config/{model}/schema`, `GET /config/{model}/{id}`

### Add a row

Steps:

1. Press **Add**.
2. Fill in the form. It shows every editable column. Under places the row below another. A link is a searchable list. A name has a row for each language.
3. Press **Save**.

Result: the row belongs to the company you work at, and the engine reads it from now on.

Refused when:

- `NotAllowedError: only an administrator changes the configuration.`
- "Configuration belongs to a company: choose one of its nodes."
- `<column> is not a valid <kind>.`
- `NotConfiguredError: there is no such <row> for this company.` Every link must point at a row the company sees.
- `NotConfiguredError: the database refuses it (<constraint>).`
- A rule of the list itself: see the list's own page.

API: `POST /config/{model}`, and `GET /config/{model}/choices/{column}?search=` for a link's choices, at most 200.

### Change a row

Steps:

1. Press the row.
2. Change the form and press **Save**.

Result: only the columns sent change, and the old row stays as a history copy.

Refused when: as for adding, and `There is no such <model> row.`

API: `POST /config/{model}` with the row's `id`.

### Switch a row off

Steps:

1. Press the row, then **Switch off...**
2. Confirm: "The engine stops reading it. Nothing is deleted: its history stays."

Result: the row becomes inactive.

Refused when: as for adding.

API: `DELETE /config/{model}/{id}`

## Lists that are also a Migration step

The screens for Posting scenarios, VAT groups, VAT return boxes, VAT return box rules, the Chart of accounts and Document types show **Export**, **Template** and **Import** on top, for those with Migration in their menu. They work as in [Migration](migration.md).

## Related

- [Migration](migration.md)
- [Menu access](../users-and-rights/menu-access.md)
- [Configuration lists in accounting](../accounting/configuration-lists.md)

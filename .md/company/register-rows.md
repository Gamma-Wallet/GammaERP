# Register rows

Cost centres, employees and users use one register screen. So do clients and suppliers, products, units and accounts. This page is what they share. `{register}` below is `cost-centers`, `employees` or `users`.

**Where:** Company › Cost centres, Company › Employees, Users and rights › Users. **Who:** read needs Read all, Read own, Write or Forward on a category. Changing needs Write on one of the row's categories. An administrator may do everything. See [Rights](../users-and-rights/rights.md).

The screen has categories on the left, rows in the middle, and the open row on the right. When rows of a category sit under each other, and there are at most 2000, the list is a tree with **Expand all** and **Collapse all**.

## Actions

### Find a row

Steps:

1. Choose a category on the left. A folder can't be chosen.
2. Type in **Search by code or name**.
3. With more than one type in the category, press type chips to narrow the list.

Result: the rows show, 50 to a page unless they are a tree. A category you have no right on does not show. "No category is open to you." shows when none do.

API: `GET /{register}/categories`, `GET /{register}/types`, `GET /{register}?category=&type=&search=&page=&pageSize=`

### Add a row

Steps:

1. Choose the category and press **New**.
2. On Details fill in:
   - Code. Empty takes the type's next code.
   - Type.
   - Under: the row it belongs under. Empty: at the top.
   - Name, in both languages.
   - Description.
   - Categories: the row is listed in each.
   - The type's properties.
3. Press **Save**.

Result: the row is saved for the cost centre you work at, and the other tabs appear.

Refused when:

- "A name is required."
- "An element is in at least one category."
- "You may not write elements of this category." (403)
- "The type does not exist."
- `TypeError: an element's type must be a child of its parent's type or the same; one without a parent needs a top-level type.`
- `NameError: an element of this type with this name is already here in this category.`
- `CodeLimitError: the type has used all its codes.`
- A required property is empty or its value is wrong: the message names the property.

API: `POST /{register}`

### Change a row

Steps:

1. Press the row.
2. Change the fields and press **Save**.

Result: the old state stays as a history copy.

Refused when: as for adding, and `NameError: an element with this name and description is already here.`, or "An element cannot be placed under itself or under one below it."

API: `GET /{register}/{id}`, `POST /{register}` with the row's `id`.

### Switch a row off

Steps:

1. Open the row and press **Switch off...**
2. Confirm.

Result: "It leaves the lists and can no longer be chosen. Nothing is deleted: its history stays."

Refused when:

- `ChildrenExistError: the element has active elements under it.`
- "You may not write elements of this category." (403)

API: `DELETE /{register}/{id}`

### Choose who may see a row

Steps:

1. Open the row, then the **Access** tab.
2. Choose Users and Roles.
3. Press **Save**.

Result: a user whose right on the category is Read own sees only rows given here to them or to one of their roles. A user with Read all, Write or Forward on the category still sees every row of it.

API: `POST /{register}` with `accessUserIds` and `accessRoleIds`.

### Read a row's history

Steps:

1. Open the row, then the **History** tab.

Result: its earlier versions, newest first, with what changed (Code, Name, Description, Type, Parent). A version with none of those shows "Properties or links changed".

API: `GET /{register}/{id}/history`

## Related

- [Cost centres](cost-centres.md)
- [Employees](employees.md)
- [Users](../users-and-rights/users.md)
- [Types and categories](../system-settings/types-and-categories.md)

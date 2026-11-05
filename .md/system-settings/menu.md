# Menu

The company's menu tree. Folders, and items that open a screen. A company's menu comes from its starter pack and is a list of rows like any other.

**Where:** System settings › Menu. **Who:** an administrator. The node is marked for administrators, and reading the list is refused to anyone else.

The rows are shown as a tree. The screen's generic actions are in [Configuration screens](configuration-screens.md).

Configuration: `tbl_menu`, model `menus`.

| Field | Column | Meaning |
|---|---|---|
| Under | `IdSup` | The folder above it. |
| Name | `Name` | The name, in both languages. |
| Code | `Code` | Its code. |
| Is element | `IsElement` | Ticked: an item that opens a screen. Unticked: a folder. |
| Sort order | `SortOrder` | Its order among its siblings. |
| Screen | `Screen` | The key of the screen it opens. |
| Parameter | `Parameter` | What the screen shows. |
| Document category | `DocumentCategoryId` | For a documents item: the category whose document types it lists. |
| Icon | `Icon` | The name of its icon. |
| Admin only | `AdminOnly` | Only administrators see it, and it can't be given. |

Screen keys and where they lead:

| Screen | Parameter | Opens |
|---|---|---|
| `documents` | none, a Document category | The category's document list. |
| `entities` | a register, such as `products` | The register. |
| `setup` | a register | The register on its categories and types. |
| `config` | a configuration model, such as `vat-groups` | That list. |
| `report` | a report, such as `trial-balance` | The report. |
| `pos` | `sme` | The touch point of sale. |
| Others | none | Their own screen, such as `migration`, `files`, `rights`, `dashboard`. |

A screen key with no built screen shows "This screen is being built."

## Actions

### Add a folder or an item

Steps:

1. Press **Add**.
2. Fill in the Name. Choose Under, and tick Is element for an item.
3. For an item, fill in the Screen and Parameter, or the Document category.
4. Press **Save**.

Result: administrators see it at once on their next menu read. Others see it when it comes under a node they were given, or once it is given. See [Menu access](../users-and-rights/menu-access.md).

Refused when:

- "A menu item opens a screen: name it, or the document category it lists."
- `NotAllowedError: only an administrator sees the whole menu.`
- as in [Configuration screens](configuration-screens.md).

API: `GET /config/menus`, `POST /config/menus`

### Change or switch off a node

Steps:

1. Press the row.
2. Change it and press **Save**, or press **Switch off...**

Result: the tree changes. A node switched off stops showing, and so does a folder left with nothing under it.

API: `POST /config/menus` with `id`, `DELETE /config/menus/{id}`

### What a document item does

It lists the types of its category that the user was given, live, so a new type needs no menu change. With several types it opens as a folder. With none it is left out.

## Related

- [Menu access](../users-and-rights/menu-access.md)
- [Home and dashboard](../getting-started/home-and-dashboard.md)
- [Configuration screens](configuration-screens.md)

## Open points

- The model `menu-access` (`tbl_inter_menu_user`) can be read and written through `/config/menu-access` by an administrator too. No menu item opens it. Rights is the screen for it.

# Rights

What a user or a role may do with each category of each register. The same screen also gives the menu: see [Menu access](menu-access.md).

**Where:** Users and rights › Rights. **Who:** an administrator. The node is marked for administrators (`AdminOnly`): it is shown to nobody else and can't be given.

Rights are rows of `tbl_elements_access`: one user or one role, one register, one category. A user's rights on a category are their own and their roles', added together. An administrator has them all.

| Right | Column | What it does |
|---|---|---|
| Read all | `ReadAll` | Shows every row of the category. |
| Read own | `ReadOwn` | Shows only the rows given to the user or their roles on the row's Access tab. |
| Write | `Write` | Adds, changes and switches off rows of the category, and shows them all. |
| Forward | `Forward` | Shows every row of the category. The code gives it no other effect. |

A request also sees only rows of its node and below: see [Architecture](../architecture.md#row-level-security-and-tenants).

The registers listed: Partners, Products and services, Cost centres, Accounts, Units of measure, Opportunities, Employees, Users, Transaction types, Documents. A register with no category does not show.

For documents, a user needs the right on the document category and the document type given to them. See [Document types](../system-settings/document-types.md).

## Actions

### Give rights

Steps:

1. Choose **Roles** or **Users**.
2. Choose the role or the user.
3. Keep the view on **Registers**.
4. Tick the rights per category.
5. Press **Save**.

Result:

- The rights of this user or role become exactly these. Rows with no right ticked are not saved, and rights no longer ticked are switched off.
- The menu ticks of the other view are saved by the same button.
- If you changed your own rights, the screens read them again.

Refused when:

- `NotAllowedError: only an administrator changes rights and where users work.`
- "There is no such user or role."
- `There is no entity <table>.`
- `<table> has no such category.`

API:

- `GET /users/{id}/rights`, `PUT /users/{id}/rights`
- `GET /roles/{id}/rights`, `PUT /roles/{id}/rights`

The body is a list of `table`, `categoryId`, `readAll`, `readOwn`, `write`, `forward`.

### See your own rights

The screens use this to show only what you may do. A user sees their rights added together. An administrator gets no list, and `isAdministrator`.

API: `GET /account/rights`

## Related

- [Menu access](menu-access.md)
- [Roles](roles.md)
- [Register rows](../company/register-rows.md): the Access tab.
- [Document types](../system-settings/document-types.md)

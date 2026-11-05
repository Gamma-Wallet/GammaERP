# Chart of accounts

The company's accounts. Each account is a row of the chart register, in a tree: a group account has accounts under it.

**Where:** Accounting setup › Chart of accounts. **Who:** the Chart of accounts item in your menu, plus your register rights on the chart's category (Users and rights › Rights: read all, read own, write). Administrators pass.

Each country's chart keeps its own account names in the country's language. One chart category is marked statutory (`tbl_chart_category.is_statutory`): it is the chart scenarios post to. Another chart gets its entries through account mappings (`tbl_inter_chart_chart`), set in Accounting setup › Account mapping. See [account-mapping.md](account-mapping.md).

## Actions

### Find an account

Steps:
1. Choose a category on the left.
2. Search by code or name. Filter by type with the chips, when there is more than one type.
3. Use **Expand all** or **Collapse all** on the tree.

Result: nothing changes.

API: `GET /charts?category={id}&type=&search=&page=&pageSize=`, `GET /charts/{id}`, `GET /charts/categories`, `GET /charts/types`.

### Add an account

Steps:
1. Choose a category and press **New**.
2. Fill in **Code** (empty takes the type's next code), **Type**, **Under** (the group account above it, empty for the top), **Name**, **Description**, **Categories**.
3. Fill in the type's properties, if any.
4. On **Access**, choose users or roles to limit who sees the row. Nobody chosen means everyone with the right to the category.
5. Press **Save**.

Result: a new account. Once an account has accounts under it, nothing can post to it: its balance is theirs.

Refused when:
- "A name is required."
- "An element is in at least one category."
- "You may not write elements of this category."
- `NameError`: another account of this type has this name in this category.
- `TypeError`: the type must be a child of the parent's type or the same; an account with no parent needs a top-level type.
- `CodeLimitError`: the type has used all its codes.
- "An element cannot be placed under itself or under one below it."

API: `POST /charts`.

### Change an account

Steps:
1. Click the account. Change the fields and press **Save**.
2. The **History** tab lists the earlier versions.

Result: the row changes and the old version stays.

Refused when: the same as adding.

API: `POST /charts` with its id. `GET /charts/{id}/history`.

### Switch an account off

Steps:
1. Click the account, press **Switch off…** and confirm.

Result: the account leaves the lists. Nothing is deleted.

Refused when:
- `ChildrenExistError`: it has active accounts under it.
- "You may not write elements of this category."

API: `DELETE /charts/{id}`.

### Export, template and import

Steps: as on [configuration-lists.md](configuration-lists.md), with the Migration step `CHART`.

API: `GET /migration/CHART/export`, `GET /migration/CHART/template`, `POST /migration/CHART/import`.

## What refuses a posting because of the chart

- `GroupAccountError`: an entry would go to a group account. Post to an account under it, or change the scenario line.
- `MappingError`: the company posts to a second chart and this statutory account has no mapping there, or has more than one. Fix it in [Account mapping](account-mapping.md).

## Related

- [account-mapping.md](account-mapping.md)
- [chart-setup.md](chart-setup.md)
- [posting-scenarios.md](posting-scenarios.md)
- [statement-lines.md](statement-lines.md)
- [../reports/financial/trial-balance.md](../reports/financial/trial-balance.md)

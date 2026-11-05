# Chart types and categories

The set-up of the chart register: its categories (charts and folders of them) and its types, with the properties each type asks for. In the Albanian pack, for example, the chart is one category and the account classes are its types.

**Where:** Accounting setup › Chart types and categories, or the **Set up** button on Chart of accounts. **Who:** an administrator, or a user with the write right on a chart category. Register rights are set under Users and rights › Rights. Saving a category or a type also needs Accounting setup › Chart types and categories in your menu, checked on the server: `NotAllowedError: this screen is not in your menu.` Administrators pass.

## Actions

### Add or change a category

Steps:
1. Press **Category**, or **Edit** on a category's row.
2. Fill in **Name**, **Code**, **Under** (empty for the top).
3. Tick **Holds rows** for a category that lists accounts. Leave it off for a folder of categories.
4. Save.

Result: the category tree changes. Only a category that holds rows can be chosen on Chart of accounts.

Refused when:
- "A name is required."
- `NameError`: a category with this name is already at this level.
- `ChildrenExistError`: switching off a category with active categories under it.
- `ElementsExistError`: moving a category that has accounts.

API: `GET /charts/categories`, `POST /charts/categories`.

### Mark the statutory chart

The statutory chart category (`tbl_chart_category.is_statutory`) is the chart the reports read and the account mapping maps into. A company has one.

**Who:** administrators. The box shows only to them, and only on a saved chart category.

Steps:
1. Press **Edit** on a chart category's row.
2. Tick, or untick, "Statutory chart: the one the reports read and the account mapping maps into".
3. Save.

Result: ticking it marks this category and takes the mark from the company's other chart categories. Unticking removes the mark.

Refused when:
- "The chart category is not one of the company's."
- You are not an administrator.

API: `GET /charts/categories/statutory` gives `{ id }` of the statutory category. `POST /charts/categories/{id}/statutory?statutory=true|false`. Both for administrators only.

### Filter the types by category

Steps:
1. Click a category's row. The types list keeps the types of that category and those under it.
2. Click it again, or press **All types**, to see them all.

Result: nothing changes.

API: `GET /charts/types`.

### Add or change a type

Steps:
1. Press **Type**, or click a type.
2. Fill in the type's form, with its properties, and save.

Result: accounts of this type ask for those properties. A property that looks up an account or a value is how a scenario line finds the account a cash desk or bank holds.

Refused when:
- "A name is required."
- `NameError`: a type with this name is already at this level, or a property is there twice.
- `CodeLimitError`: the next code must lie between the first and the last.
- `ChildrenExistError`: switching off a type with active types under it.

API: `POST /charts/types`. Copying properties from another type: `POST /charts/types/{id}/properties/copy`.

## Related

- [chart-of-accounts.md](chart-of-accounts.md)
- [scenario-lines.md](scenario-lines.md)

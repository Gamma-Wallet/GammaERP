# Products

The register of everything the company sells, buys or keeps: goods, services and assets.

**Where:** Products and services › Products and services. **Who:** the menu item in your menu (Users and rights › Rights); the read or write right on the product's category.

The screen has three parts: categories on the left, the category's rows in the middle, the open row on the right. The base pack gives the categories Products, Services and a folder Assets (Land, Buildings, Office equipment, Vehicles, Computer equipment, Furniture and fittings, Intangible assets), and the types Goods, Service and Asset. Goods has one property, Barcode.

## Actions

### Find a product

Steps:
1. Click a category. A folder cannot be clicked: it holds other categories, not rows.
2. Type in "Search by code or name", or click a type chip.

Result: the list shows the matching rows, 50 to a page. When the category's rows sit under one another and there are at most 2000 of them, the list is a tree with Expand all and Collapse all.

Refused when: you have no read right on any category. The screen says "No category is open to you."

API: `GET /products?category={id}&type={id}&search={text}&page=1&pageSize=50`

### Add a product

Steps:
1. Click the category, then New. The New button shows only with the write right on that category.
2. Fill the Details tab:
   - Code. Empty takes the type's next code.
   - Type. Only the types linked to the row's categories are offered; when none is linked, every type.
   - Under. Another row of this list, or empty for the top.
   - Name, Description. One text per language.
   - Categories. The row is listed in each.
   - VAT group. What its lines start with; empty uses the type's.
   - "Composite: a sale takes the ingredients of its recipe instead of the product" (`isComposite`). Tick it for a dish or a set sold from its recipe. See [Recipes](../stock/recipes.md).
   - Properties. The ones its type has.
3. Click Save.

Result: the product exists and the panel stays open with the tabs Details, Units, Access and History. No stock, price or account changes.

Refused when:
- No name: "A name is required."
- No category: "An element is in at least one category."
- No write right on any of its categories: "You may not write elements of this category."
- Another row of the same type, name and category at this level: NameError.
- The type does not fit the parent: TypeError. Under a parent, the row's type is a child of the parent's type, or the same type. At the top, it needs a top-level type.
- The type has used its code range: CodeLimitError, "the type has used all its codes."
- A required property is empty, or a value does not fit its kind: the field is named.

API: `POST /products` with no `id`.

### Change a product

Steps:
1. Click the row. The panel opens on Details.
2. Change what is needed and click Save.

Result: the row changes and the old state is kept as a history copy. Documents already made keep the name they were saved with.

Refused when: as for adding, and when another row has the same name and description at this level (NameError).

API: `POST /products` with its `id`. Read one with `GET /products/{id}`.

### Give a product to chosen users or roles

Steps:
1. Open the row, tab Access.
2. Choose users and roles, then Save.

Result: nobody chosen means everyone with the right to the category sees the row. Otherwise users with only "read own" see it when they or their role are chosen.

API: `POST /products` with `accessUserIds` and `accessRoleIds`.

### See its history

Steps: open the row, tab History.

Result: the earlier versions, newest first, each with what changed (Code, Name, Description, Type, Parent) or "Properties or links changed".

API: `GET /products/{id}/history`

### Switch a product off

Steps:
1. Open the row and click Switch off…
2. Confirm.

Result: it leaves the lists and can no longer be chosen. Nothing is deleted.

Refused when: rows sit under it (ChildrenExistError), or you have no write right on its categories.

API: `DELETE /products/{id}`

### Load products from a file

Steps: use the Migration menu, steps PRODUCT_CATEGORIES, PRODUCTS and PRICES.

Result: described with the Migration menu.

API: `POST /migration/{code}/import`

## Related

- [Product units](product-units.md)
- [Types and categories](product-types-and-categories.md)
- [Sale prices](sale-prices.md)
- [Recipes](../stock/recipes.md)

# Product types and categories

Categories decide where a product is listed and who may see it. Types decide its properties, its code range, its VAT group, whether it tracks serial numbers or lots, and whether it is a fixed asset.

**Where:** System settings › Types and categories › Products and services, or the Set up button on Products and services › Products and services. **Who:** the Set up button shows for an administrator, or for anyone with the write right on some product category. Saving a category or a type, and copying properties, also needs System settings › Types and categories › Products and services in your menu, checked on the server: `NotAllowedError: this screen is not in your menu.` Administrators pass.

The set-up view has two trees: Categories on the left, Types on the right. Clicking a category filters the types; clicking it again, or All types, clears the filter.

## Actions

### Add or change a category

Steps:
1. Click Category for a new one, or Edit on a row.
2. Fill Name, Code and Under (empty: at the top).
3. Tick "Holds rows", or leave it off for a folder of categories.
4. Click Save.

Result: the category is in the tree. Rights are given per category, on Users and rights › Rights.

Refused when:
- No name: "A name is required."
- The same name at this level: NameError.
- It is placed under itself or one below it.
- It has products and is moved: ElementsExistError, "the category has elements; it cannot be moved."

API: `GET /products/categories`, `POST /products/categories`

### Switch a category off

Steps: open it, click Switch off…, confirm.

Result: it leaves the tree. Nothing is deleted.

Refused when: active categories sit under it (ChildrenExistError).

API: `POST /products/categories` with `active: false`.

### Add or change a type

Steps:
1. Click Type for a new one, or click a row.
2. Fill:
   - Name, Code, Under.
   - Codes from, Codes to. Rows of the type get the next code, made of the type's code and a number.
   - Categories. The categories whose rows may have this type.
   - VAT group. What the lines of its products start with, unless a product names its own.
   - Tracks serial numbers: each unit has its own number on every line.
   - Tracks lots: every line names its lot. Then also "An expired lot can't go to a client", on by default.
   - Fixed asset: "its products are registered and depreciated". Only products of a type with this ticked can be [registered as assets](../fixed-assets/registering-an-asset.md). It is set on each type and not taken from a type above it. Saved in `products.tbl_product_type.fixed_asset`. The starter packs tick it on the Asset type.
   - "Rows can have this type". Off makes it a folder of types.
3. Add properties (below).
4. Click Save.

Result: the type is saved with its properties. New rows of the type show them. The tracking settings apply from the next document line.

Refused when:
- No name: "A name is required."
- The same name at this level, for a new type: NameError.
- The next code lies outside the range: CodeLimitError.
- A category does not exist.
- A property name is there twice: NameError.

API: `GET /products/types`, `POST /products/types`

### Add a property to a type

Steps:
1. In the type, click Property.
2. Give its Name and Kind: Text, Number, Date, Date and time, List (one choice), List (several choices), Choices that show more fields, Lookup (a row of a register), Several texts, Location on a map, Photo.
3. For a list, add its Choices. For a lookup, choose what it Picks from.
4. Optional: Shown, to show it only when a choice of an activating list is picked. That list and its choice must be saved first.
5. Tick Required if needed. Order with the arrows, remove with the bin.
6. Save the type.

Result: the property shows on every row of the type. A removed property is switched off.

Refused when: a kind is unknown; a property has no name; a choice has no name; Shown does not name a saved choice of another activating list of this type.

API: part of `POST /products/types`.

### Copy properties from another type

Steps:
1. Open a saved type.
2. Choose a type in "Copy from a type…" and click Copy.

Result: the other type's properties and their choices are added. Those the type already has, by name, stay as they are.

API: `POST /products/types/{id}/properties/copy` with `fromTypeId` and, for only some, `propertyIds`.

### Switch a type off

Steps: open it, click Switch off…, confirm.

Result: new rows can no longer have this type. Nothing is deleted.

Refused when: active types sit under it (ChildrenExistError).

API: `POST /products/types` with `active: false`.

## Open points

- Switching tracking on for a type that already holds stock leaves that stock without lots or numbers. Give them on the Lots and Serial numbers screens.

## Related

- [Products](products.md)
- [Product units](product-units.md)
- [Lots](../stock/lots.md)
- [Serial numbers](../stock/serial-numbers.md)

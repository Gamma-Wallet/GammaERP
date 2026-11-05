# Partner types and categories

Categories say what a partner is to the company and who may see it. Types say which properties it has and how it is coded. Sale prices can also depend on both.

**Where:** System settings › Types and categories › Clients and suppliers, or the Set up button on Clients and suppliers. **Who:** the Set up button shows for an administrator, or for anyone with the write right on some partner category. Saving a category or a type, and copying properties, also needs System settings › Types and categories › Clients and suppliers in your menu, checked on the server: `NotAllowedError: this screen is not in your menu.` Administrators pass.

| Base pack | Rows |
|-----------|------|
| Categories | Clients, Suppliers, Leads |
| Types | Unknown, Person, SME, LTD, Company of the group |
| Properties of each type | Tax number, City, Address, Phone, E-mail |

## Actions

### Add or change a category

Steps:
1. Click Category for a new one, or Edit on a row.
2. Fill Name, Code and Under. Tick "Holds rows", or leave it off for a folder.
3. Click Save.

Result: the category is in the tree. Rights are given per category, on Users and rights › Rights.

Refused when:
- No name: "A name is required."
- The same name at this level: NameError.
- It is placed under itself or one below it.
- It has partners and is moved: ElementsExistError.

API: `GET /partners/categories`, `POST /partners/categories`

### Add or change a type

Steps:
1. Click Type for a new one, or click a row.
2. Fill Name, Code, Under, Codes from, Codes to and Categories.
3. Tick "Rows can have this type", or leave it off for a folder of types.
4. Add properties with Property: name, kind, choices, Required, Shown.
5. Click Save.

Result: new rows of the type show its properties, and take the next code when Code is empty.

Refused when:
- No name; the same name at this level, for a new type; a property named twice: NameError.
- The next code lies outside the range: CodeLimitError.

API: `GET /partners/types`, `POST /partners/types`

### Copy properties from another type

Steps: open a saved type, choose one in "Copy from a type…", click Copy.

Result: its properties are added. Those the type already has, by name, stay as they are.

API: `POST /partners/types/{id}/properties/copy`

### Switch a category or type off

Steps: open it, click Switch off…, confirm.

Result: a category leaves the tree. New rows can no longer have a switched-off type. Nothing is deleted.

Refused when: active rows of its own kind sit under it (ChildrenExistError).

API: the same save route with `active: false`.

## Related

- [Partners](partners.md)
- [Product types and categories](../products/product-types-and-categories.md), which describes the property kinds in full
- [Sale prices](../products/sale-prices.md)

# Units of measure

The register of units: piece, kg, g, m, cm and so on. Units form trees: cm, mm and km sit under m. A general conversion works only inside one tree.

**Where:** Products and services › Units of measure. Set-up: System settings › Types and categories › Units of measure. **Who:** the menu item in your menu; the read or write right on the unit's category. The Set up button shows for an administrator, or for anyone with the write right on some unit category. Saving unit categories and types also needs System settings › Types and categories › Units of measure in your menu, checked on the server.

The base pack gives the categories and types Standard, Weight, Time and Length, and these units:
- Piece.
- kg, with g under it.
- m, with cm, mm and km under it.
- Year, with Month under it, Day under Month and Hour under Day.

## Actions

### Add a unit

Steps:
1. Click the category, then New.
2. Fill Code, Type, Under, Name and Description.
3. To convert with a unit by a general conversion, choose Under so both share the same top unit.
4. Click Save.

Result: the unit can be given to products and product types.

Refused when:
- No name, or no category.
- No write right on its categories.
- The same name, type and category at this level: NameError.
- The type does not fit the parent: TypeError. A unit may sit under a unit of its own type.

API: `POST /units`. List with `GET /units?category={id}`, read one with `GET /units/{id}`.

### Change a unit

Steps: click the row, change it, click Save.

Result: the old state is kept as history.

Refused when: as for adding.

API: `POST /units` with its `id`.

### Switch a unit off

Steps: open the row, click Switch off…, confirm.

Result: it leaves the lists. Nothing is deleted.

Refused when: units sit under it (ChildrenExistError).

API: `DELETE /units/{id}`

### See a unit's history

Steps: open the row, tab History.

API: `GET /units/{id}/history`

### Set up unit categories and types

Steps: open the set-up view. It works as for products: Category, Type, Edit and Switch off….

Result: as described in [product types and categories](product-types-and-categories.md), without the VAT group, tracking and Units parts, which are for products only.

API: `GET /units/categories`, `POST /units/categories`, `GET /units/types`, `POST /units/types`

### Load units from a file

Steps: use the Migration menu, step UNITS.

API: `POST /migration/UNITS/import`

## Related

- [Unit conversions](unit-conversions.md)
- [Product units](product-units.md)
- [Convert stock between units](../stock/convert-stock.md)

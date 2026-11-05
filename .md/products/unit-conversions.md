# Unit conversions

How many of one unit make one of another: 1 kg = 1000 g. A row is for every product, or for one product. Each row also works the other way round.

**Where:** Products and services › Unit conversions. **Who:** the menu item in your menu. Anyone signed in can read the rows; only an administrator adds, changes or switches them off.

Configuration table: `tbl_unit_conversion`. The base pack gives kg to g, m to cm, m to mm, km to m, day to hour and year to month.

## Actions

### Add a conversion

Steps:
1. Click Add.
2. Fill:
   - From unit, To unit.
   - Factor. How many To one From is.
   - Product. Empty: every product.
3. Click Save.

Result: Convert stock between units can use it.

Refused when:
- The factor is not above 0, or both units are the same: "A conversion is between two units, by a factor above 0."
- A row for every product joins two trees of units: "A conversion for every product is between units of one tree (cm and m, not cm and kg); name the product for one across trees."
- You are not an administrator.
- A link points at a row the company does not see: NotConfiguredError.

API: `POST /config/unit-conversions`. Read with `GET /config/unit-conversions`.

### Change or switch off a conversion

Steps:
1. Click the row.
2. Change it and click Save, or click Switch off… and confirm.

Result: the old row is kept as history. Documents already posted do not change.

API: `POST /config/unit-conversions` with its `id`, `DELETE /config/unit-conversions/{id}`.

### Read the factor between two units

Steps: on Convert stock between units, choose the product and the two units. The screen shows the factor.

Result: the product's own row is used first, then the row for every product, which counts only inside one tree. The same units give 1.

Refused when: there is no conversion. The API answers not found, and the screen says "These units do not convert: add a unit conversion in Configuration first."

API: `GET /stock/factor?from={id}&to={id}&product={id}`. This needs the Convert stock between units screen.

## Examples

- General: From kg, To g, Factor 1000, Product empty.
- One product across trees: From Bottle, To l, Factor 0.7, Product this wine.

## Related

- [Units of measure](units.md)
- [Convert stock between units](../stock/convert-stock.md)

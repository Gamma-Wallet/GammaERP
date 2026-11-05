# Product units

The units a product is sold, bought and kept in. A product can have more than one. The first is the default a document line proposes. A product with none of its own is offered its type's units.

**Where:** Products and services › Products and services, open a row, tab Units. For a type: the type editor, section Units. **Who:** for a product, the write right on one of its categories. For a type, an administrator.

Stock, prices and cost layers are kept per product and unit, so 1 kg and 1000 g of a product are two separate stocks until they are converted.

## Actions

### Set a product's units

Steps:
1. Open the product, tab Units.
2. Add a unit with "Add a unit…". Use "Use the type's units" to copy the type's list when the product has none.
3. Put them in order by dragging, or with the arrows. The first shows Default.
4. Remove a unit with the bin. The bin is off for a unit marked In use.
5. Click "Save the units". The Save button at the bottom of the panel does not save this list.

Result: the list is saved whole, in the order shown. New units are added and removed ones are switched off. A document line now offers these units, the first one proposed.

Refused when:
- A unit in use is left out: "In use, so it stays: …". In use means stock, a balance, a price, a cost layer, an asset or a document line points at it.
- The unit does not exist for the company: "There is no unit …".
- No write right: "You may not write products of this category."

API: `GET /products/{id}/units`, `PUT /products/{id}/units` with the unit ids in order.

### Set a product type's units

Steps:
1. Open System settings › Types and categories › Products and services, and click the type.
2. In the Units section, add, order and remove units as above.
3. Click "Save the units". They are saved on their own, apart from the type's Save.

Result: products of the type with no units of their own are offered this list.

Refused when:
- You are not an administrator: "Only an administrator sets a product type's units."
- A sale price by partner uses the unit: "A sale price uses it, so it stays: …".
- There is no such type or unit.

API: `GET /products/types/{id}/units`, `PUT /products/types/{id}/units`

## When posting adds a unit

When a posted document first moves a product in a unit, posting makes that product and unit row. So converting stock into a new unit also adds the unit to the product's own list, marked In use.

## Related

- [Products](products.md)
- [Units of measure](units.md)
- [Unit conversions](unit-conversions.md)
- [Sale prices](sale-prices.md)

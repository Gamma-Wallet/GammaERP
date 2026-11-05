# Convert stock between units

Turn stock at a cost centre from one unit into another: 1 kg into 1000 g. Each unit of a product is a separate stock, so this is how goods bought in one unit are kept or sold in another. It is a Unit split document, numbered and posted.

**Where:** Stock and production › Convert stock between units. The documents made are listed under Documents › Production. **Who:** the Convert stock between units screen in your menu, and the Unit split document type given to you.

## Actions

### Convert stock

Steps:
1. Choose At, the cost centre.
2. Choose Product, From unit and To unit. Once a product is chosen, the unit lists offer only its units: its own, or else its product type's. Before that they offer every unit.
3. Give the Quantity, in the from unit, and the Date. Empty means today.
4. Read the preview, for example 1 kg = 1000 g.
5. Click Convert. The button stays off while the units do not convert.

Result:
- A Unit split document is made and posted, and the screen opens it.
- Two lines: the quantity out in the from unit, and the converted quantity in, in the to unit. The converted quantity is rounded to 6 decimals.
- Each cost layer converts on its own. Its value stays and its unit cost changes by the same factor: 1 kg at 10 € becomes 1000 g at 0.010 €.
- The value and the books do not change.
- The cost centre stays the same.

Refused when:
- The quantity is not above 0: "The quantity is above 0."
- Both units are the same: "Choose another unit to split into."
- There is no conversion: NotConfiguredError, "there is no conversion between these units."
- The company has more than one unit split type: "the company has N document types for this; choose one."
- There is too little stock: see [negative stock](negative-stock.md).
- The product tracks lots: "tracks lots and can't go through production or a stock conversion."
- The product tracks serial numbers: the lines need their numbers, and this screen cannot give them.
- At posting, the quantity is not exactly the converted one: "… make … in this unit, not …". This cannot happen from the screen.

API: `POST /stock/convert` with `costCenterId`, `productId`, `fromUnitId`, `toUnitId`, `quantity`, `date`. The API also takes `note` and `typeId`.

### Reverse a conversion

Steps: open the document under Documents › Production and reverse it as any posted document.

Result: a Reversal of unit split document. The converted stock goes and the stock in the first unit comes back.

Refused when: part of the converted stock was already sold or moved: "what was sold or moved is reversed first."

API: `POST /documents/{id}/reverse`

When the company has more than one unit split type, the screen shows **Document type** to choose one (`GET /api/v1/stock/convert/types`).

## Related

- [Reversing a document](../documents/reversing.md)
- [Unit conversions](../products/unit-conversions.md)
- [Product units](../products/product-units.md)
- [Cost layers](cost-layers.md)

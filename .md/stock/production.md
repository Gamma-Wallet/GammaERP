# Orders to produce

Make a product from its recipe at a cost centre, or make what a customer's order needs. Each production is a posted financial document, so stock and the books move together.

**Where:** Stock and production › Orders to produce. The screen has three tabs: Produce, Orders to produce and Disassemble (see [Disassembly](disassembly.md)). The documents made are listed under Documents › Production. **Who:** the Orders to produce screen in your menu, and the Production document type given to you.

The base pack gives the document type Production, with the line types Raw material used, Produced item and Waste. It has no reversal type.

## Actions

### Work out the ingredients

Steps:
1. On the Produce tab, choose Product, Unit and Quantity.
2. Choose Made at, the cost centre. Without it, Held here stays empty.
3. Click "Work out ingredients".

Result: a table of ingredients. Nothing is saved. For each row:
- Quantity: what leaves stock, waste included. It is the produced quantity × the recipe's quantity, plus the waste.
- Waste: the part of that quantity which is lost.
- Held here: what the cost centre holds, in the ingredient's unit. A row is marked "short" when Held here is below its Quantity. The quantity already counts the waste.

The recipe rows for the chosen unit are used, or else the rows for any unit. A product with no recipe shows "The product has no recipe: add the ingredients used below."

Refused when: the cost centre is not one you see ("The place does not exist.") or is in no company. Only Produced on the production point display may deliver to a place of the company the user does not see; see [Production point display](../point-of-sale/production-point/display.md).

API: `GET /production/calculate?product={id}&unit={id}&quantity={n}&costCenter={id}`

### Add or remove ingredients by hand

For a product with no recipe, or an ingredient the recipe does not list.

Steps:
1. Work out the ingredients: the form to add them shows under the table.
2. Choose **Ingredient**, **Unit** and **Quantity** (above 0), then click **Add ingredient**. An ingredient already in the table is replaced by the new row. Its waste is 0 and Held here stays empty.
3. To take a row out, click the bin at the end of it.

Result: only the table changes. Nothing is saved until Produce. With no rows left, Produce cannot be pressed.

### Produce from a recipe

Steps:
1. Work out the ingredients.
2. Change the Quantity or Waste of a row if the real use differed. Add or remove rows if needed.
3. Choose the Date. Empty means today.
4. Click Produce.

Result:
- A Production document is made, numbered and posted, and the screen opens it.
- One Raw material used line per ingredient, for quantity less waste. One Waste line for each ingredient with waste. One Produced item line.
- Ingredients leave the cost centre at their cost, oldest layers first.
- The product comes in at the same cost centre, at what the raw materials cost. Waste is not part of that cost.
- The waste goes out of stock as an expense.
- The accounts move as the document type's posting scenarios say.

Refused when:
- No cost centre: "Choose where it is made."
- The quantity is not above 0: "The quantity is above 0."
- No ingredient is given and the recipe gives nothing for this unit: NotConfiguredError, "the product has no recipe for this unit; give the ingredients used." Add the ingredients by hand.
- An ingredient is the product itself, its quantity is not above 0, or its waste is negative or above its quantity.
- The company has more than one production type: "the company has N document types for this; choose one."
- The type has no line type for raw materials, products made or waste: NotConfiguredError.
- There is too little stock: see [negative stock](negative-stock.md).
- An ingredient tracks lots: "tracks lots and can't go through production or a stock conversion."
- An ingredient tracks serial numbers: the line needs its numbers, and this screen cannot give them.
- A closed period, or any other rule of saving and posting a document.

API: `POST /production` with `costCenterId`, `productId`, `unitId`, `quantity`, `date`, `lines`. An empty `lines` takes the recipe's. The API also takes `orderId`, `note`, `toCostCenterId` and `typeId`, which the screen does not send.

### See the orders waiting

Steps:
1. Open the Orders to produce tab.
2. Choose Order type. It lists only the top-level document types whose settings say "Its orders are listed on the Production screen, to be produced" (`ProducedOnProductionScreen`, see [Document types](../system-settings/document-types.md)). The starter packs tick it for Sales order.

Result: the active orders of that type with products that have a recipe and something still to make, oldest first. Each shows its code, date, client and, per product and unit, what is left: the ordered quantity less what the production documents made from the order have made (their lines of the "made" stock direction). An order partly produced stays with what is left.

API: `GET /production/order-types` (the types offered), `GET /production/orders?type={id}`

### Produce an order

Steps:
1. Choose Made at, and the Date if needed.
2. Click Produce on the order.

Result:
- One posted Production document per product of the order that has a recipe, for what is left of it. Each is linked to the order.
- All are made or none.
- The ingredients are the recipe's, unchanged.
- The order leaves the list, and a message names the documents made.

Refused when:
- The order has nothing left to produce: "The order has nothing left to produce."
- Any of the refusals of Produce from a recipe.

API: `POST /production/orders/{id}?costCenter={id}&date={day}`

### A production is not reversed

A production is not reversed: making a product cannot be undone, and the order it was made for stays produced. The starter packs give no reversal type for it, so the Reverse button does not show and the server refuses with `NotConfiguredError`. A production entered by mistake is corrected with a separate document that takes the product apart again: see [Disassembly](disassembly.md).

When the company has more than one production (or disassembly) type, the Produce and Disassemble tabs show **Document type** to choose one (`GET /api/v1/production/types?kind=production|disassembly`).

## Related

- [Reversing a document](../documents/reversing.md)
- [Disassembly](disassembly.md)
- [Recipes](recipes.md)
- [Cost layers](cost-layers.md)
- [Negative stock](negative-stock.md)
- [Lots](lots.md)

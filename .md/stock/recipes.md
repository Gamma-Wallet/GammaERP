# Recipes

What one of a product is made of. A recipe has one row per ingredient. Recipes serve three uses: production, disassembly (the recipe read backwards), and composite products that give up their ingredients when sold.

**Where:** Stock and production › Recipes. **Who:** the menu item in your menu. Anyone signed in can read the rows; only an administrator adds, changes or switches them off.

Configuration table: `products.tbl_product_recipe` (config screen `product-recipes`). Recovered percent is the column `recovered_percent`.

## Actions

### Add an ingredient to a recipe

Steps:
1. Click Add.
2. Fill:
   - Product. The product being made.
   - Unit. The product's unit the quantities are for. Empty: any unit.
   - Component. The ingredient.
   - Component unit. The ingredient's unit.
   - Quantity. How much of it one of the product takes, before waste.
   - Waste percent. The share lost in making it: 10 takes 1.1 times the quantity.
   - Recovered percent. The share of the ingredient that comes back when the product is taken apart, 0 to 100. Default 100. The rest is waste.
   - Sort order.
3. Click Save.

Result: the row is saved. Later productions and sales use it. Documents already made do not change.

Refused when:
- The Recovered percent is below 0 or above 100: "What comes back on taking the product apart is 0 to 100 %."
- The quantity is not above 0, the waste is below 0, or the ingredient is the product itself: "An ingredient is another product, in a quantity above 0, with a waste of 0 % or more."
- You are not an administrator.
- A link points at a row the company does not see: NotConfiguredError.

API: `POST /config/product-recipes`. Read with `GET /config/product-recipes?productId={id}`.

### Change or switch off an ingredient

Steps:
1. Click the row.
2. Change it and click Save, or click Switch off… and confirm.

Result: the old row is kept as history. A switched-off row is no longer read.

API: `POST /config/product-recipes` with its `id`, `DELETE /config/product-recipes/{id}`.

## How a recipe is used

Rows for the line's unit are used if there are any. Otherwise the rows with no unit are used. Recipes go one level deep: an ingredient that is itself made here is produced first.

### In production

Any product with recipe rows can be produced. It does not need to be composite. See [orders to produce](production.md).

### In a disassembly

There is no separate disassembly recipe. The same rows are read backwards: each ingredient comes back for the quantity taken apart × its quantity × Recovered percent ÷ 100, and the rest is lost. Waste percent is not used. See [Disassembly](disassembly.md).

### In a sale of a composite product

A product is marked composite on its form in Products and services › Products and services, box "Composite: a sale takes the ingredients of its recipe instead of the product" (`isComposite`), saved with the product (`POST /products`). See [Products](../products/products.md).

A product marked composite, with recipe rows, gives up its ingredients instead of itself when a line moves it. Each ingredient leaves stock for the line's quantity × its quantity × (1 + waste percent ÷ 100). The line's cost is what those ingredients cost.

This holds for lines that move stock from one side to the other. A raw material used or a product made stays itself.

A line that follows a bill line a production point marked as produced sells the product itself, when its document type has "sells produced product" on. The stock came in as the product, so the ingredients do not leave again.

A composite product is not itself checked for serial numbers or lots.

Refused when: an ingredient tracks lots: "… tracks lots and can't be used in a recipe."

## Related

- [Orders to produce](production.md)
- [Disassembly](disassembly.md)
- [Products](../products/products.md)
- [Lots](lots.md)
- [Cost layers](cost-layers.md)

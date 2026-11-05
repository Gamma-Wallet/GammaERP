# Shop

A till: scan barcodes or tap menu buttons, then pay at once.

**Where:** Point of sale › Open the point of sale, at a Shop. **Who:** the screen `pos`.

## Actions

### Scan a barcode

A barcode is a text property with the Code `BARCODE` on a product's type. The name is not read, so it can be renamed and translated. The base pack has it on the Goods type. See [Property codes the system reads](../system-settings/types-and-categories.md#property-codes-the-system-reads). The screen reads all barcodes once when it opens, so a barcode added later needs the screen opened again.

**Steps**
1. Scan into the box, which keeps the focus after every line. A scanner sends the code and Enter. Typing a code and pressing Enter works too.

**Result:** the product joins the basket at its price. Scanning it again adds one to the line.

**Refused when**
- "No product has the barcode {code}."
- "{product} has no sale price."
- "No lot of {product} is held here.": see [lot-picker.md](lot-picker.md).

**API:** `GET /pos/barcodes`

### Add from the menu

**Steps:** tap a category, then a product. This uses the same sale menus as a restaurant.

**Result:** one more of it in the basket. A product with no price cannot be tapped.

**API:** `GET /pos/menus/current?pointOfSale=`

### Change or clear the basket

**Steps:** use plus and minus on a line, or press Clear. At zero a line goes.

**Result:** the basket changes on screen only. Nothing is saved until Pay.

### Pay

**Steps**
1. Press Pay.
2. Finish on the [pay screen](paying.md).

**Result:** one bill to the walk-in client is saved and posted, with no table. Stock leaves the shop and the client owes the amount. The pay screen opens on this bill. If you leave without paying, the bill stays in [Unpaid](paying.md).

**Refused when**
- "{product} fits none of the lines of {type}: check its category in the products register."
- "{product} has no unit to sell in: give it one in the products register."
- "Open a shift first: this point of sale has none open."
- Stock or period refusals from posting.

**API:** `POST /documents`, `POST /documents/{id}/post`

### Work at a till

A shop can have tills below it (Company › Cost centres, the shop's Point of sale tab, *Its places*: kind Till). A cashier is connected to the till they sign in at (the founder, 2026-11-04).

**Steps**
1. An administrator gives the cashier the till as a place to work at: the user's Cost centres tab.
2. The cashier signs in and chooses the till.
3. The shop screen opens. Its header shows the shop and the till.

**Result:**
- The screen works as the shop: the shop's settings, menus, buttons and barcodes, and its stock. A bill's lines leave from the shop.
- Each bill records the till (`TableId`) and belongs to the till (`OwnerId`).
- The payment goes to the till's cash desk, if the till has one of its own; otherwise to the shop's.
- The Shift view opens, counts and closes the till's own shift. See [shifts.md](shifts.md).
- The unpaid list shows what the user sees: a cashier at a till sees that till's bills; someone signed in at the shop sees every till's.

Signed in at the shop itself, the screen works as before.

API: `GET /pos/settings?pointOfSale={till}` gives the shop as `pointOfSaleId`, its name as `pointOfSale` and the till as `tillId`. `GET /pos/point-of-sale-of?place=` gives the point of sale a place works as.

## Related

- [paying.md](paying.md)
- [lot-picker.md](lot-picker.md)
- [settings.md](settings.md)
- [shifts.md](shifts.md)

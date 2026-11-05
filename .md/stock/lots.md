# Lots

Which batch of a product is where, and when it expires. A lot says where the batch is; stock and cost stay under the product. Nothing alerts anyone: expiry is read by hand on this screen.

**Where:** Stock and production › Lots, with the tabs Find and Assign to stock. Lots on lines are in the document editor. **Who:** the Lots screen in your menu for the screen. A document line needs only the right to the document.

Tables: `tbl_product_lot` (a lot, once per product in a company) and `tbl_product_lot_stock` (how much of it each holder has, per unit).

## Actions

### Switch lot tracking on

Steps:
1. Open System settings › Types and categories › Products and services and click the type.
2. Tick "Tracks lots: every line names its lot".
3. Leave "An expired lot can't go to a client" on, or switch it off.
4. Click Save.

Result: every line that moves a product of this type now needs a lot at posting.

API: `POST /products/types` with `tracksLot` and `refusesExpired`.

### Give a lot on a document line

Steps:
1. Pick a product that tracks lots. A Lot box and an expiry date appear under it.
2. On a purchase, type the lot and its expiry date.
3. On anything else, click into the Lot box. It lists the lots the from side holds, earliest expiry first, each with its quantity and expiry. Nothing is chosen for you.
4. Post the document.

Result: on posting, the line's quantity of the lot moves from the from side to the to side. A new lot is made the first time. The expiry is saved when the lot has none yet. A partner's holding is kept as well, to show who has what.

A draft can be saved without a lot; the check runs at posting.

Refused when, at posting:
- No lot: "… tracks lots: give its lot on the line."
- The expiry differs from the lot's: "lot … of … expires on …".
- The to side is a partner, the type refuses expired lots, the document is not a reversal, and the lot expired before the posting date: "lot … of … expired on …".
- The from side is not a partner and holds less of that lot: "there are … of lot … of … here, … are needed." This holds whatever the negative stock setting says. A partner is never refused, as a supplier holds none.
- The line goes through production or a stock conversion: "… tracks lots and can't go through production or a stock conversion."

API: `lot` and `lotExpiryDate` on the line of `POST /documents`, then `POST /documents/{id}/post`. Held lots: `GET /lots/held?product={id}&holder={id}&unit={id}`.

### Pick a lot at the point of sale

Steps: tap a product that tracks lots. The cashier picks one of the lots held at the point of sale.

Result: the order line carries that lot.

Refused when: none is held there: "No lot of … is held here."

API: `GET /lots/tracked`, `GET /lots/held`. These need no Lots screen.

### Find a lot

Steps:
1. Open the Find tab.
2. Type in Search: a lot, product or holder.
3. Optional: Expiring within days. Empty shows all. 0 shows only lots already expired (expiry day before today). 1 also shows lots that expire today; each day more reaches one day further.

Result: what is held of each lot, soonest expiry first, at most 500 rows: lot, product, expiry date, held by, unit, quantity. Holders can be cost centres, partners or employees. Lots with nothing held are left out. With a number of days, lots with no expiry date are left out too.

Refused when: you work above a company: "Lots belong to a company: choose one of its nodes."

API: `GET /lots?search={text}&expiresBy={day}`

### Give a lot to stock already held

For stock that came in before tracking was on, or was loaded at opening.

Steps:
1. Open the Assign to stock tab. It lists tracked products at each cost centre, with Quantity and With a lot.
2. Click a row. The title shows how much is without a lot.
3. Fill Lot, Expiry date and Quantity.
4. Click "Give the lot".

Result: the lot is made if new, and the quantity is added to what that cost centre holds of it. The stock and its cost stay as they are. No document is made.

Refused when:
- The product does not track lots: "This product does not track lots: set it on its type first."
- No lot, or the quantity is not above 0: "Give a lot and a quantity above zero."
- It would pass what the cost centre holds: "The place holds …, of which … have a lot: … more do not fit."
- The expiry differs from the lot's: "Lot … expires on …".

API: `GET /lots/stock`, `POST /lots/assign` with `costCenterId`, `productId`, `unitId`, `lot`, `expiryDate`, `quantity`.

## Lots in production

Production and stock conversion use stock nobody picks a lot for, so they are refused for now. Three cases:

| Case | Message |
|------|---------|
| A raw material or waste line of a product that tracks lots | "tracks lots and can't go through production or a stock conversion" |
| Any line of a production or unit split with a product that tracks lots | The same message |
| A composite product sold, with an ingredient that tracks lots | "… tracks lots and can't be used in a recipe." |

A composite product is not itself tracked, even when its type tracks lots.

## Reversals

A reversal copies the line's lot and expiry date. The expired-lot refusal does not apply to it.

## Related

- [Document lines](../documents/lines.md)
- [Lot picker at the point of sale](../point-of-sale/lot-picker.md)
- [Product types and categories](../products/product-types-and-categories.md)
- [Orders to produce](production.md)
- [Recipes](recipes.md)
- [Negative stock](negative-stock.md)

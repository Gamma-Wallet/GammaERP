# Lot picker

For a product that tracks lots, the cashier chooses which lot is sold. Nothing is chosen for them.

**Where:** any sale view of the point of sale, when a product that tracks lots is added. **Who:** the screen `pos`.

## Actions

### Choose a lot

**Steps**
1. Tap or scan the product. A box opens with the lots held at this point of sale, each with its lot number, quantity and expiry date if it has one.
2. Tap a lot, or Cancel to add nothing.

**Result:** one of the product joins the order or basket with that lot. Two lots of one product are two lines. The lot stays on the line through the order and the bill.

**Refused when:** "No lot of {product} is held here."

If the list of products that track lots can't be read when the screen opens, the screen says "The products that track lots could not be read, so no lot is asked for: {reason}" and carries on: no lot box opens until the screen is opened again.

**API:** `GET /lots/tracked` (read once when the screen opens), `GET /lots/held?product=&holder=`

## Related

- [tables.md](tables.md)
- [shop.md](shop.md)

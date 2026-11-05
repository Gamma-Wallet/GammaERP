# Sale prices

The prices proposed when a product is sold. A price is a configuration row. It only proposes: the person can change the price on the line.

**Where:** Products and services › Sale prices, and Products and services › Sale prices by partner. **Who:** the menu item in your menu. Anyone signed in can read the rows; only an administrator adds, changes or switches them off.

| Screen | Table | A row is for |
|--------|-------|--------------|
| Sale prices | `tbl_product_sale_price` | One product |
| Sale prices by partner | `tbl_product_sale_price_type` | A unit of a product type |

## Actions

### Add a sale price

Steps:
1. Open Sale prices and click Add.
2. Fill:
   - Product.
   - Product specific unit. Empty: any unit.
   - Partner category and Partner type. Empty: everyone.
   - Currency. Empty: any currency.
   - Value. The price the line proposes.
   - Value LCU.
3. Click Save.

Result: the row exists in the company. New lines propose it. Documents already made do not change.

Refused when:
- You are not an administrator: "only an administrator changes the configuration."
- A link points at a row the company does not see: NotConfiguredError, "there is no such … for this company."
- A value is not of the column's kind, for example text in Value.

API: `POST /config/sale-prices`. Read with `GET /config/sale-prices`, `GET /config/sale-prices/{id}`.

### Change or switch off a sale price

Steps:
1. Click the row.
2. Change it and click Save, or click Switch off… and confirm.

Result: the old row is kept as history. A switched-off row is no longer read.

Refused when: as for adding.

API: `POST /config/sale-prices` with its `id`, `DELETE /config/sale-prices/{id}`.

### Add a sale price by partner

Steps:
1. Open Sale prices by partner and click Add.
2. Fill Product type unit, Partner category, Partner type, Currency, Value and Value LCU.
3. Click Save.

Result: the row is saved. A product type's unit with such a row cannot be removed from the type.

Refused when: as for a sale price.

API: `POST /config/sale-price-types`, `DELETE /config/sale-price-types/{id}`.

### Load prices from a file

Steps: use the Migration menu, step PRICES.

Result: described with the Migration menu.

API: `POST /migration/PRICES/import`

## How a line proposes a price

The line asks for a proposal when its product is picked (`GET /documents/line-proposal`, with the document's `date`; none is today).

On a document whose to side is a partner (a sale), it reads Sale prices of the company for that product. It keeps the rows that fit, where an empty column fits everything:
- the partner's type,
- one of the partner's categories,
- the line's unit.

Then the most particular row wins, compared in this order: partner type, partner category, unit, and then a row in the document's own currency before one in another. Among equals, the row made last wins.

**The price is always worked out at the rates of the document's day** (the founder, 2026-11-04): the row's Value × the rate of its currency on that day ÷ the rate of the document's currency on that day. A row with no currency is in the company's currency. The rate is the latest one on or before the day, as a document takes it. A row whose currency has no rate on the day is passed over and the next row that fits is taken; with none left the line proposes no price. The stored Value LCU is not read: it is the value on the day the row was set, shown as *Value LCU when set*.

When the product has no Sale price that fits, the line reads Sale prices by partner: the rows for the line's unit on the product's type, only those the company's configuration reads (its own, or its group's above it). The same fit rules apply (partner type, one of the partner's categories; an empty column fits everything). The closest match to the partner wins, in this order: partner type, then partner category, then the document's own currency. Among equals, the row made last wins. Its value is worked out at the day's rates in the same way.

A product that is a fixed asset proposes what is left of its value instead.

On a document whose from side is a partner (a purchase), the line proposes the last price paid for that product and unit.

The point of sale shows the latest sale price with no partner category and no partner type, in the company's currency at today's rates (a price whose currency has no rate today is passed over for the one before it). It does not read Sale prices by partner: it always sells to the general client.


## Related

- [Products](products.md)
- [Product units](product-units.md)
- [Sale menus](sale-menus.md)
- [Partner types and categories](../partners/partner-types-and-categories.md)

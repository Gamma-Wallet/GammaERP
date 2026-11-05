# Stock

What each holder has now, valued by what is left of its cost layers.

**Where:** Reports › Stock and sales › Stock. **Who:** the `stock` report in your menu.

## Actions

### Show the stock

Filters: **Held by** (Places, Clients and suppliers, Staff, All; it opens at Places), **Cost centre**, **Product category**.

Steps:
1. Set the filters and press **Show**.

Shows:
- Figures: Items held, Stock value, Below zero (lines with a negative quantity).
- Table: Held by, Item, Name, Unit, Quantity, Unit cost, Value.

Clients, suppliers and staff show their running position: what they received is positive, what they delivered negative. With a cost centre chosen, only places are shown. There is no date filter: it is the position now.

Result: nothing changes.

Refused when: "Name the holder: place, partner, staff or all."

API: `GET /reports/stock?holder=&costCenter=&productCategory=`. Excel: `/reports/stock/xlsx`. PDF: `/reports/stock/pdf`, with `holder`, `costCenter` and `productCategory`. Excel and PDF follow **Held by**, like the screen.

## Related

- [item-movements.md](item-movements.md)
- [../../fixed-assets/assets-at-clients.md](../../fixed-assets/assets-at-clients.md)

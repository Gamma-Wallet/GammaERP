# Item movements

A product's movements in a period, from its purchase through transfers to its sale. Also called item tracking.

**Where:** Reports › Stock and sales › Item movements. **Who:** the `item-tracking` report in your menu.

## Actions

### Show a product's movements

Filters: **Product** (required), **From**, **Up to**.

Steps:
1. Choose the product, set the period and press **Show**.

Shows:
- Figures: Movements, Quantity moved.
- Table: Date, Document, Type, From, To, Quantity, Unit, Value, in posting order.

It lists every line of a posted document with this product. Each line's own from and to are used, else the document's.

Result: nothing changes. Clicking a row opens the document.

API: `GET /reports/item-tracking?product=&from=&to=`. Excel: `/reports/item-tracking/xlsx`. PDF: `/reports/item-tracking/pdf`, same parameters.

## Related

- [stock.md](stock.md)
- [purchase-price-history.md](purchase-price-history.md)

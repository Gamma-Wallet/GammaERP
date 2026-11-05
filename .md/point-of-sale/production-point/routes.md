# Production routes

Which production point makes what. A route is a row in `tbl_production_route`. No route ships in the base pack.

**Where:** Point of sale › Production routes. **Who:** anyone with the menu item can read; only an administrator changes configuration.

| Column | Meaning |
|--------|---------|
| `PointOfSaleId` | The point of sale it is for. Empty: all |
| `ProductCategoryId` | A product category |
| `ProductId` | One product |
| `ProductionPointId` | The production point, a cost centre. Required |
| `WarningMinutes` | Minutes until a card shows late. The point uses the lowest of its rows |

## Which route wins

For each order line, routes must match the point of sale (or be for all) and the product (or a category it is in). Then:

1. A row for the product goes before a row for its category.
2. Then a point of sale's own row goes before one for all.

A row with neither product nor category matches nothing.

## Actions

### Add a route

**Steps**
1. Open Point of sale › Production routes and add a row.
2. Choose a product or a category, the production point, and optionally the point of sale and warning minutes.
3. Save.

**Result:** new order lines and open ones go to that point, because the route is read each time. Lines listed under *Ordered with no production point* appear on its display.

**Refused when:** you are not an administrator ("only an administrator changes the configuration").

**API:** `GET /config/production-routes`, `POST /config/production-routes`, `DELETE /config/production-routes/{id}`

## Related

- [display.md](display.md)
- [settings.md](settings.md)

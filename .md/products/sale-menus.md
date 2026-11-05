# Sale menus

What the point of sale shows and when: a morning menu, an evening menu. A menu has opening hours and shows product categories.

**Where:** Products and services › Sale menus, and Products and services › Sale menu categories. **Who:** the menu item in your menu. Anyone signed in can read the rows; only an administrator adds, changes or switches them off.

Both are configuration rows: `tbl_sale_menu` and `tbl_sale_menu_category`.

## Actions

### Add a sale menu

Steps:
1. Open Sale menus and click Add.
2. Fill:
   - Code and Name.
   - Starts at. Empty: from midnight.
   - Ends at. Empty: until midnight. An end before the start runs past midnight.
   - Point of sale. Empty: every point of sale of the company.
   - Sort order.
3. Click Save.

Result: the menu exists. It shows nothing until categories are added to it.

Refused when:
- You are not an administrator.
- A link points at a row the company does not see: NotConfiguredError.

API: `POST /config/sale-menus`. Read with `GET /config/sale-menus`.

### Put a product category on a menu

Steps:
1. Open Sale menu categories and click Add.
2. Fill Sale menu, Product category and Sort order.
3. Click Save.

Result: while the menu is open, the point of sale shows that category and its products, each with its latest sale price for everyone.

Refused when: as above.

API: `POST /config/sale-menu-categories`

### Change or switch off a row

Steps:
1. Click the row.
2. Change it and click Save, or click Switch off… and confirm.

Result: the old row is kept as history. A switched-off row is no longer read.

API: `POST /config/{model}` with its `id`, `DELETE /config/{model}/{id}`. The model is `sale-menus` or `sale-menu-categories`.

### See what is open now

Steps: open the point of sale. It reads the menus open at that moment.

Result: the menus for this point of sale or for every point of sale, in sort order, with their categories and products.

API: `GET /pos/menus/current?pointOfSale={id}&at={time}`

## Related

- [Point-of-sale settings](../point-of-sale/settings.md)
- [Sale prices](sale-prices.md)
- [Product types and categories](product-types-and-categories.md)

# Repair shop

A point of sale of the kind Repair shop has no sale screen. Opening it goes to the repair board.

**Where:** Point of sale › Open the point of sale, at a Repair shop. **Who:** the screens `pos` and `repairs`.

## Actions

### Open the repair shop

**Steps**
1. Choose the repair shop on the entry page.
2. Open Point of sale › Open the point of sale.

**Result:** the settings say the screen is `repairs`, so the Repair board opens. This comes from the `Screen` column of the kind's row in `tbl_point_of_sale_setting`. The base pack also sets `RequiresShift` false there.

The board can be opened directly in Repairs › Repair board, which is what the Technician role has.

**API:** `GET /pos/settings?pointOfSale=`

## Related

- [../repairs/README.md](../repairs/README.md)
- [settings.md](settings.md)

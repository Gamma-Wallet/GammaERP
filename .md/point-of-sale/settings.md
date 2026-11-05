# Point-of-sale settings

What a point of sale works with: which document types it writes, which cash desk and client it uses, and whether it needs a shift. Everything is rows in `tbl_point_of_sale_setting`.

**Where:** Point of sale › Point-of-sale settings, and Company › Cost centres › the point of sale › tab Point of sale. **Who:** anyone with the menu item can read; only an administrator can change configuration.

## How a value is chosen

Three levels of rows, read column by column. An empty column takes the next level's value.

1. The point of sale's own row (`PointOfSaleId` set).
2. Its kind's row (`PointOfSaleTypeId` set).
3. The company's default row (both empty).

A place is a point of sale when its kind has a row. A place below one, such as a shop's till, works as that point of sale: its own row comes first, then the point of sale's own row, its kind's and the default's. A till's row usually sets only its own cash desk and whether bills need a shift.

| Column | Screen label | Meaning | Required | Base pack |
|--------|--------------|---------|----------|-----------|
| `BillTypeId` | Bill | Document type of a bill | Yes | Point-of-sale receipt (default row) |
| `OrderTypeId` | Order sent first, bill at payment | What a waiter sends. Empty: the bill itself is sent | No | Point-of-sale order (restaurant row) |
| `MoneyInTypeId` | Money in | Takes a bill's money into the cash desk | Yes | Point-of-sale receipt of money |
| `MoneyOutTypeId` | Money out | Pays money out of the cash desk | Yes | Point-of-sale payment |
| `CashDeskId` | Cash desk | The cash desk cost centre | Yes | Empty: must be set |
| `WalkInClientId` | Client of a walk-in bill | The client every bill goes to | Yes | General client |
| `PaymentMethodId` | Paid by default | Method proposed when paying | No | Cash (default row) |
| `ProductId` | A night is sold as | Product of a hotel night. Shown only for a hotel | Hotel only | Empty: must be set for check-out |
| `Screen` | Screen | Tables, Shop, Cinema / theatre, Hotel or Repair shop (`tables`, `shop`, `events`, `stays`, `repairs`). Empty takes its kind's | No | One per kind |
| `RequiresShift` | Bills need an open shift | Yes, No, or empty to take its kind's or the company's. Empty at every level means they do | No | False for a repair shop, otherwise empty |

The Point of sale screen lists a missing required column by name and will not open until it is set: bill, money in, money out, cash desk, walk-in client.

## Actions

### Create a point of sale

**Steps**
1. Open Company › Cost centres and add a row.
2. Choose its kind: Restaurant / café, Shop, Cinema / theatre, Hotel or Repair shop. Give it a name.
3. Save.

**Result:** the cost centre exists and opens on its Point of sale tab. The tab appears for any cost centre whose type is named by a settings row.

**API:** `POST /cost-centers`

### Set what a point of sale works with

**Steps**
1. Open the point of sale's Point of sale tab.
2. Under *What it works with*, pick values. An empty field shows in grey what its kind or the company sets.
3. Press Save the settings.

**Result:** the point of sale's own row is saved. All ten columns can be set here: the seven above, plus *Order sent first, bill at payment*, *Screen* and *Bills need an open shift*. An empty one takes its kind's or the company's.

**Refused when:** you are not an administrator ("only an administrator changes the configuration"). The fields are disabled for others.

**API:** `POST /config/point-of-sale-settings`

### Set the kind's or company's settings, or any other column

Use this for the rows of a kind or of the company default. The point of sale's own row can be set here too, or on its tab.

**Steps**
1. Open Point of sale › Point-of-sale settings.
2. Add or edit the row of the point of sale, kind or company default.
3. Save.

**Result:** the row is saved and every point of sale that inherits the column follows it.

**Refused when:** you are not an administrator.

**API:** `GET /config/point-of-sale-settings`, `POST /config/point-of-sale-settings`, `DELETE /config/point-of-sale-settings/{id}`

### Add a place

Places are cost centres below the point of sale. The kind of a place decides what may go under it.

| Point of sale | Goes under it |
|---------------|---------------|
| Restaurant / café | Hall, Counter. Table goes under a Hall |
| Shop | Till |
| Cinema / theatre | Auditorium, then Row, then Seat |
| Hotel | Floor, then Room |
| Repair shop | Nothing |

**Steps**
1. On the Point of sale tab, under *Its places*, pick where it goes in *Under*.
2. Pick the kind of place. *Nothing goes here* means this place takes no children.
3. Type a name and press Add.

**Result:** a new cost centre under the chosen place. The list shows the tree.

**Refused when:** you are not an administrator. The row is hidden for others.

**API:** `POST /cost-centers`

Places can also be added directly in Company › Cost centres. A table's position and seats, a seat's position and a room's beds are properties of the cost-centre type (`tbl_cost_center_type_info`).

### Give a till its own cash desk

**Steps**
1. Open Company › Cost centres and the till.
2. On its Point of sale tab, choose its *Cash desk*, and if needed *Bills need an open shift*. Empty takes the shop's, shown in grey.
3. Press Save the settings.

**Result:** a settings row for the till. Its shift counts its own drawer. A till with no cash desk of its own uses the shop's, and only one shift can be open on that desk at a time.

**Refused when:** you are not an administrator.

### Decide what is on sale

Products reach the screen through sale menus, not one by one. A menu lists product categories and can have opening times; times that end before they start run past midnight, and no times means all day. A menu with no point of sale serves all of them. Categories in several open menus show once.

Set these in Products and services › Sale menus and Sale menu categories. Prices are in Products and services › Sale prices.

**API:** `GET /pos/menus/current?pointOfSale=&at=`

## Related

- [overview.md](overview.md)
- [shifts.md](shifts.md)
- [tables.md](tables.md)
- [production-point/settings.md](production-point/settings.md)

The grey hint under *A night is sold as* names the product the kind or company sets. A place added here is named in English and in the language you work in.

## Open points


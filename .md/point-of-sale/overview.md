# Point of sale overview

A point of sale is a cost centre. Its kind, chosen when the cost centre is created, decides which screen it opens and which places it can hold.

**Where:** Point of sale › Open the point of sale. **Who:** the screen `pos` in your menu (Menu access), or an administrator.

## The five kinds

Each kind is a cost-centre type under the folder type Point of sale (`tbl_cost_center_type`). The screen it opens is the `Screen` column of its row in `tbl_point_of_sale_setting`. One point of sale can choose another on its own tab, field "Screen"; empty takes its kind's.

| Kind (cost-centre type) | Screen | Opens on | Places it holds | Detail |
|-------------------------|--------|----------|-----------------|--------|
| Restaurant / café (`POS_RESTAURANT`) | `tables` | Floor plan | Halls with tables, counters | [tables.md](tables.md) |
| Shop (`POS_SHOP`) | `shop` | Scan box and basket | Tills | [shop.md](shop.md) |
| Cinema / theatre (`POS_EVENTS`) | `events` | List of shows | Auditoriums with rows and seats | [events.md](events.md) |
| Hotel (`POS_STAYS`) | `stays` | Rooms by day | Floors with rooms | [stays.md](stays.md) |
| Repair shop (`POS_REPAIRS`) | `repairs` | The repair board | None | [repair-shop.md](repair-shop.md) |

The base pack ships these five. A company can have several points of sale of the same kind.

## The customer is never identified

The point of sale does not ask who the customer is. Every bill goes to the client in the setting *Client of a walk-in bill*. In the base pack this is the General client (`partner:GENERAL`), set on every kind's row. At a cinema or hotel a typed guest name only replaces the client's name shown on that bill and is held on the booking; it makes no client and looks none up.

A customer who must be known gets a normal sales document, not a point-of-sale bill.

## What a sale writes

All of this is the document types in the point of sale's [settings](settings.md):

| Step | Document | Effect |
|------|----------|--------|
| Order sent at a table | Order type (pack: Point-of-sale order). Not posted: billed at the till | No stock, no balances |
| Bill | Bill type (pack: Point-of-sale receipt). Posted | Stock leaves the point of sale, the client owes the amount |
| Payment | Money-in type (pack: Point-of-sale receipt of money). Posted | One document per client into the cash desk, linked to the bills it pays |
| Money outside a bill | Money-in or money-out type. Posted | Cash desk moves, see [money-in-out.md](money-in-out.md) |

Prices shown are the product's latest sale price for everyone (`tbl_product_sale_price` with no client category or type), plus VAT. At a table the VAT is the product's own rate, from its line proposal, once the product has been added to an order; until then its tile uses the bill type's first VAT rate. See [tables.md](tables.md).

## Actions

### Open the point of sale

The screen opens at the place you work at, which is the cost centre you chose after signing in.

**Steps**
1. Choose the point of sale on the entry page.
2. Open Point of sale › Open the point of sale.

**Result:** the screen of its kind opens. The top bar shows the place, the view buttons (Tables or Sale, Unpaid, Money in / out, Shift), your name and a leave button. A repair shop goes straight to the repair board.

**Refused when**
- *Choose where you work*: no place is chosen. The button leads to the entry page.
- *This point of sale is not set up yet*: a required setting is empty, listed by name. An administrator gets a button to the settings; others are told to ask one.
- *Open a shift*: the point of sale requires a shift and none is open. See [shifts.md](shifts.md).

**API:** `GET /pos/settings?pointOfSale=`, `GET /pos/places?pointOfSale=`, `GET /pos/menus/current?pointOfSale=`, `GET /pos/shift/current?pointOfSale=`. The first two do not need the `pos` screen, because a point of sale's setup reads them too.

### Leave the point of sale

**Steps:** press the leave button at the top right.

**Result:** you are back on the home page. Nothing changes. A basket or order not sent is lost.

## Rights

- **Screen:** every `/pos` call except settings and places is refused with "this screen is not in your menu" unless your menu holds the `pos` screen.
- **Document types:** a user who is not an administrator must have the bill, order and money types given to them or their role, in System settings › Document types, on the type's users tab. Otherwise saving is refused with "this document type has not been given to you or to one of your roles".
- **Roles:** the base pack has the roles Waiter and Cashier. Waiter gets Point of sale, the order, receipt and receipt of money types, and Write on Orders, Sales and Cash desk. Cashier gets the same plus Shifts and the Point-of-sale payment type. An administrator can change them. See [Roles](../users-and-rights/roles.md).

## Related

- [settings.md](settings.md)
- [paying.md](paying.md)
- [shifts.md](shifts.md)
- [production-point/README.md](production-point/README.md)
- [../repairs/README.md](../repairs/README.md)

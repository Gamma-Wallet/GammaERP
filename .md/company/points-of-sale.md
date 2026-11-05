# Points of sale

A point of sale is a cost centre. It is created by its type: the type says what kind it is, which screen it opens and which places it has.

**Where:** Company › Cost centres. **Who:** Write on the category to add it. Administrator to save its settings and add places on this tab. The rest of the point of sale's work is under the Point of sale menu.

A cost-centre type counts as a kind of point of sale when `tbl_point_of_sale_setting` has a row naming it (`PointOfSaleTypeId`).

| Kind | Screen | Places |
|---|---|---|
| Restaurant / café | tables | Hall, then Table; Counter |
| Shop | shop | Till |
| Repair shop | repairs | none |
| Cinema / theatre | events | Auditorium, Row, Seat |
| Hotel | stays | Floor, Room |

## Actions

### Add a point of sale

Steps:

1. In Company › Cost centres press **New**.
2. Choose the Type, one of the kinds above. Point of sale itself is a folder and can't be chosen.
3. In **Under** choose the company.
4. Fill in the Name, Address, Phone and E-mail.
5. Press **Save**.

Result: the cost centre is made, and the panel opens on its **Point of sale** tab.

Refused when: as in [Register rows](register-rows.md).

API: `POST /cost-centers`

### Set what it works with

The **Point of sale** tab, part "What it works with". An empty field takes what its kind or the company sets, shown in grey.

| Field | Column of `tbl_point_of_sale_setting` |
|---|---|
| Bill | `BillTypeId` |
| Money in | `MoneyInTypeId` |
| Money out | `MoneyOutTypeId` |
| Cash desk | `CashDeskId` |
| Client of a walk-in bill | `WalkInClientId` |
| Paid by default | `PaymentMethodId` |
| A night is sold as (a hotel only) | `ProductId` |
| Order sent first, bill at payment | `OrderTypeId`: the order type a waiter sends; empty sends the bill itself |
| Screen | `Screen`: Tables, Shop, Cinema / theatre, Hotel or Repair shop; empty takes its kind's |
| Bills need an open shift | `RequiresShift`: Yes, No, or empty to take its kind's or the company's |

Steps:

1. Choose the fields. Those still missing are marked required.
2. Press **Save the settings**.

Result: the point of sale gets its own settings row, over its kind's row and the company's row.

Refused when: `NotAllowedError: only an administrator changes the configuration.`, or a choice is not a row of this company.

API: `GET /pos/settings?pointOfSale=`, `GET /config/point-of-sale-settings`, `POST /config/point-of-sale-settings` with `pointOfSaleId`, `GET /config/point-of-sale-settings/choices/{column}`

### Add its places

The same tab, part "Its places". The tree shows the places it has.

Steps:

1. In the first list choose what the new place goes under: the point of sale or one of its places.
2. In the second list choose the kind of place. The list offers the types under that place. "Nothing goes here" means there are none.
3. Type the Name and press **Add**.

Result: a new cost centre under that place, in the first category of its type. The name is saved in English and copied to the company's language.

Refused when: as in [Register rows](register-rows.md).

API: `GET /pos/places?pointOfSale=`, `POST /cost-centers`

### Default settings

The rows with no point of sale named, for the kinds and for the whole company, are in Point of sale › Point-of-sale settings.

## Related

- [Cost centres](cost-centres.md)
- [Register rows](register-rows.md)
- [Point of sale](../point-of-sale/README.md)

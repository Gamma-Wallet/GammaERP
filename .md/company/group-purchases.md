# Purchases from the group

A sale from the group is the company's purchase. Posting the sale makes an unposted purchase in the company's books, for the company to check and post by hand. This screen says which sale makes which purchase.

**Where:** Company › Purchases from the group. **Who:** anyone with it in their menu reads. An administrator changes rows. The screen's generic actions are in [Configuration screens](../system-settings/configuration-screens.md).

Configuration: `tbl_group_purchase_setting`, model `group-purchase-settings`. A book without a row takes part in nothing.

| Field | Column | Meaning |
|---|---|---|
| Code | `Code` | Pairs the group's row with the company's row. |
| Sale type | `SaleTypeId` | At the group: this document type makes a purchase. |
| Purchase type | `PurchaseTypeId` | At the company: the document type of the purchase. |
| Warehouse | `WarehouseId` | Where the goods arrive. Empty: the company chooses it on the draft. |

The starter pack has two rows: `GOODS` (sales invoice, goods, with purchase invoice, goods) and `SERVICES`. Document type codes differ per country, so each book holds its own row.

## Actions

### Set up the pairing

Steps:

1. Working at the group, open the screen and add or change the row with the sale type.
2. Working at the company, keep a row with the same Code, with the purchase type and, if you want, the warehouse.
3. Press **Save** on each.

Result: from the next posted sale, the purchase is made.

Refused when: as in [Configuration screens](../system-settings/configuration-screens.md).

API: `GET /config/group-purchase-settings`, `POST /config/group-purchase-settings`, `DELETE /config/group-purchase-settings/{id}`

### What happens when a group's sale is posted

This runs inside the sale's own posting, in the same database transaction.

Conditions: the sale's To is the group's partner for a company directly under it, the sale's type is on a row with a Code, and the company has a row with that Code and a purchase type.

Result: an unposted purchase in the company's books:

- From is the company's partner for the group. To is the warehouse.
- External number is the sale's code. The dates are the sale's.
- Each line is found again in the company's own rows: transaction type, product and unit by code, currency by code, VAT group by its rate.
- What the company has no match for stays empty on the draft, keeping the group's name, for the company to choose. So does an exchange rate it does not have for the day. A posting checks every line again, so the draft can't be posted like that.

Refused when, which refuses the sale's posting too:

- `NotConfiguredError: the company has no partner for the group.`
- `NotConfiguredError: the company does not have the line's transaction type.`
- `NotConfiguredError: the company does not have the sale's currency.`

API: `POST /documents/{id}/post`, on the sale.

### What happens when the sale is reversed

- A draft the company has not posted is removed. What is left of the sale, after a part reversal, is made again as a draft.
- A purchase the company has posted is the company's own. The company reverses it itself.

## Related

- [Add a company to a group](../getting-started/add-a-company-to-a-group.md)
- [Configuration screens](../system-settings/configuration-screens.md)
- [Document types](../system-settings/document-types.md)

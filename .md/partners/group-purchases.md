# Group purchases

A sale from a group to one of its companies is that company's purchase. Each is the other's client and supplier, through a partner row. This page covers the partner side and the settings.

**Where:** Company › Purchases from the group. **Who:** the menu item in your menu. Anyone signed in can read the rows; only an administrator adds, changes or switches them off.

Configuration table: `tbl_group_purchase_setting`. The base pack gives the rows GOODS and SERVICES, each pairing a sale invoice type with a purchase invoice type. A book without the row takes part in nothing.

## Actions

### Link the group and its companies as partners

Steps: setting up the group's books does this. Otherwise an administrator working at the group opens the entry page › **New company** and presses **Link the group's partners again** on the card The group's partners. See [Add a company to a group](../getting-started/add-a-company-to-a-group.md#link-the-groups-partners-again).

Result: the group and each company get a partner row for one another, of type Company of the group, in their own books. The link is `GroupMemberId` on the partner, which no screen shows. Repeating it is safe.

Refused when:
- A single company has no group: SetupError.
- You are not an administrator working at the group: "only an administrator working at the group links its partners."

API: `POST /group-books/partners`. Setting up the books: `POST /group-books`.

### Set which sale makes which purchase

Steps:
1. In the group's books, open Purchases from the group and give a row its Code and Sale type.
2. In the company's books, give a row with the same Code its Purchase type, and optionally its Warehouse.
3. Click Save on each.

Result: the two rows pair by Code. Empty Warehouse means the company chooses it on the draft.

Refused when: you are not an administrator, or a link points at a row the company does not see.

API: `POST /config/group-purchase-settings`

### Sell from the group

Steps: post a sale of a paired type in the group, with the company's partner on the to side.

Result: in the same database transaction, an unposted purchase appears in the company's books:
- Its from side is the company's partner for the group.
- Its external number is the sale's code.
- Its lines are matched to the company's own rows by code; a VAT group by its rate.
- What has no match stays empty for the company to choose.

A posting checks every line again, so it cannot be posted incomplete.

Refused when: the company has no partner for the group: NotConfiguredError, "the company has no partner for the group."

API: `POST /documents/{id}/post`

### Check and post the purchase

Steps: the company opens the draft under Documents › Purchases, completes it and posts it.

Result: it posts as any purchase. The balance is on the company's partner for the group.

API: `POST /documents/{id}/post`

### Reverse the sale

Steps: reverse the sale in the group.

Result: an unposted draft goes, and what is left of the sale is made again. A purchase the company already posted stays: the company reverses it itself.

API: `POST /documents/{id}/reverse`

## Related

- [Company: group purchases](../company/group-purchases.md)
- [Partners](partners.md)
- [Balances](balances.md)

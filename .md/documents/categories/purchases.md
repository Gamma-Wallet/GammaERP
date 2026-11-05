# Purchase documents

Invoices from suppliers and customs declarations. A purchase invoice brings stock in with its cost.

**Where:** Documents › Purchases. **Who:** see [who may do what](../life-cycle.md#who-may-do-what).

## Types in the base pack

| Type | Code | From | To | Kinds of line | Open item | Partial reversal | Reversed by |
|------|------|------|----|---------------|-----------|------------------|-------------|
| Purchase invoice, goods | `PURCHASE_INVOICE_GOODS` | Supplier | Cost centre | Account, Item, Fixed asset | Yes | Yes | Credit note, goods bought |
| Purchase invoice, services | `PURCHASE_INVOICE_SERVICES` | Supplier | Cost centre | Account, Item, Fixed asset, Service item | Yes | Yes | Credit note, services bought |
| Customs declaration | `CUSTOMS_DECLARATION` | Supplier | Cost centre | Customs, Transport, Customs agency | No | No | None |
| Customs declaration, assets | `CUSTOMS_DECLARATION_ASSETS` | Supplier | Cost centre | Customs, Transport, Customs agency, for assets | No | No | None |

## Actions

### Write a purchase invoice

Steps:
1. Press **New** and pick the type.
2. Pick the supplier in **From**, the place in **To**. Type the supplier's own number in **Their number**.
3. Add lines. The last price paid for that product and unit is proposed. Serial numbers are typed here, and a new lot is typed with its expiry.
4. Save, look at the Posting tab, post.

Result: at posting the stock comes in and a cost layer is made from the line's value, the purchase price is recorded and proposed next time, the supplier's balance and the accounts move, and the invoice stays open until a payment covers it.

Refused when: see [saving](../drafts.md#save) and [posting](../posting.md#post).

API: `POST documents`, `POST documents/{id}/post`.

### Make an invoice from another document

Steps:
1. Open the offer, purchase order or contract and use **Generate**. See [flows](../flows.md).

API: `POST documents/{id}/make/{type}`.

### Check a purchase from the group

Steps:
1. Find the unposted purchase in the list. It was made when the group company posted its sale.
2. Fill in what the company has no match for: products, units, VAT groups and exchange rates are matched by code or rate, and what has no match stays empty.
3. Post.

Result: a normal purchase. Posting checks every line again, so it can't be posted with empty fields.

Refused when: `NotConfiguredError: the company has no partner for the group.` stops the sale's posting.

Configured under Company › Purchases from the group (`tbl_group_purchase_setting`).

### Write a customs declaration

Steps:
1. Pick the supplier and the place, and add Customs, Transport or Customs agency lines. The type has a property that points at the purchase invoice.
2. Save and post.

Result: posting writes the entries of its scenarios. These lines move no stock.

Both customs declarations have a reversal type in the base pack (Reversal of customs declaration, and of customs declaration, assets). A declaration moves no stock, and its reversal mirrors its entries.

## Related

- [Reversals](reversals.md)
- [Lines](../lines.md)
- [Orders](orders.md)

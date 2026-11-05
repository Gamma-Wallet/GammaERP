# Goods transfers

Stock moving between places, staff and clients. The goods keep their cost; nothing is sold.

**Where:** Documents › Goods transfers. **Who:** see [who may do what](../life-cycle.md#who-may-do-what).

## Types in the base pack

| Type | Code | From | To | Kinds of line |
|------|------|------|----|---------------|
| Goods transfer note | `GOODS_TRANSFER` | Cost centre | Cost centre | Item, Fixed asset |
| Goods transfer, cost centre to staff | `GOODS_TRANSFER_TO_STAFF` | Cost centre | Staff | Fixed asset (service), Item |
| Goods transfer, staff to cost centre | `GOODS_TRANSFER_FROM_STAFF` | Staff | Cost centre | Fixed asset (service) |
| Goods transfer, cost centre to client | `GOODS_TRANSFER_TO_CLIENT` | Cost centre | Client | Fixed asset (service), Item |
| Goods transfer, client to cost centre | `GOODS_TRANSFER_FROM_CLIENT` | Client | Cost centre | Fixed asset (service), Item |
| Point-of-sale goods in | `POS_GOODS_IN` | Staff | Cost centre | Fixed asset, Item |
| Point-of-sale goods out | `POS_GOODS_OUT` | Cost centre | Staff | Item, Fixed asset |
| Goods out to staff | `GOODS_OUT_TO_STAFF` | Cost centre | Staff | Item |
| Goods transfer, staff to client | `GOODS_TRANSFER_STAFF_TO_CLIENT` | Staff | Client | Item |
| Goods in from staff | `GOODS_IN_FROM_STAFF` | Staff | Cost centre | Item |

Fixed asset (service) lines do not move stock. The others do. Goods transfer note has a Carrier property.

## Actions

### Move goods

Steps:
1. Press **New** and pick the type.
2. Pick **From** and **To**.
3. Add lines. Pick serial numbers from what the from side holds, and name the lot for lot products.
4. Save and post.

Result: the stock leaves the from side and reaches the to side. A sending place gives up its oldest cost layers, and the receiver gets them with their cost and purchase date. A serial number's holder changes. A transfer to a client leaves a fixed asset the company's.

Refused when:
- `StockError`: not enough at the place, or in the company: see [stock checks](../lines.md#stock-and-cost-checks).
- `StockError`: {item} comes in only with a purchase bill or from another place of the company.
- Otherwise see [saving](../drafts.md#save) and [posting](../posting.md#post).

API: `POST documents`, `POST documents/{id}/post`.

Each transfer type has a reversal type in the base pack (Reversal of …, in the Reversals category): it takes off at the place the goods went to what the transfer brought there, and puts it back where it came from at the same cost.

## Open points

- Several point-of-sale types here are named for the point of sale. Whether a person starts them here or they come from the point-of-sale screens depends on who is given them.

## Related

- [Lines](../lines.md)
- [Reversing](../reversing.md)

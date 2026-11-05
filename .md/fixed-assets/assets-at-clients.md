# Assets kept at clients

An asset can sit at a client's premises and stay the company's. Moving it there is a transfer, not a sale: it keeps depreciating and nothing is sold.

**Where:** Documents › Transfers. **Who:** the rights of the document type.

The starter pack's transfer types for this:

| Document type | From | To |
|---------------|------|----|
| Goods transfer, cost centre to client | cost centre | client, supplier or third party |
| Goods transfer, client to cost centre | client, supplier or third party | cost centre |
| Goods transfer, staff to client | staff | client, supplier or third party |

None of them sells assets (`sells_assets` is off), so a line does not propose the asset's remaining value as its price.

## Actions

### Prepare the asset

Steps:
1. When you register the asset, choose its **Depreciation cost centre**. See [registering-an-asset.md](registering-an-asset.md).

Result: its depreciation keeps going to that cost centre. Without one, the run takes the place that holds the asset, and once it is at a client no place holds it, so the entry carries no cost centre.

### Move an asset to a client

Steps:
1. Make a transfer of the type above, with the asset's product on a line.
2. Post it.

Result: the quantity moves to the client. In the starter pack's scenarios the transfer posts to the same account on both sides. The asset is not marked sold and stays in depreciation runs.

API: `POST /documents`, `POST /documents/{id}/post`.

### Bring it back

Steps:
1. Make a transfer, client to cost centre, and post it.

Result: the quantity is back at the place.

### See what clients hold

Steps:
1. Open Reports › Stock and sales › Stock.
2. Set **Held by** to Clients and suppliers, or All.

Result: a row per client and product with the quantity they hold. See [stock.md](../reports/stock-and-sales/stock.md).

## Related

- [registering-an-asset.md](registering-an-asset.md)
- [selling-an-asset.md](selling-an-asset.md)
- [../reports/stock-and-sales/transfers.md](../reports/stock-and-sales/transfers.md)

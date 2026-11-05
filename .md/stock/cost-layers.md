# Cost layers, first in first out

How stock is valued. Each receipt of stock makes a layer: a quantity at a cost, on a date, at a holder. Stock going out takes the oldest layers first. Nothing here is a screen of its own: posting a document does it all, in the same database transaction as the books.

**Where:** no menu item. It is read on Reports › Stock and sales › Stock, and set on System settings › Document types. **Who:** the report in your menu for reading; an administrator for changing the configuration.

Tables: `tbl_product_cost_layer` and `tbl_product_cost_layer_use`. A layer belongs to a holder, a product and a unit. The line keeps the cost it moved in `tbl_transaction.cost_lcu`.

## Actions

### See what stock is worth

Steps:
1. Open Reports › Stock and sales › Stock.
2. Filter by holder, cost centre or product category if needed.

Result: quantity, unit cost and value per holder and product, valued by what is left of the cost layers.

API: `GET /reports/stock?holder=place&costCenter={id}&productCategory={id}`, also `/xlsx` and `/pdf`.

### Set what a document side does

Steps:
1. Open System settings › Document types and choose the type.
2. Open its from or to side row.
3. Choose Cost layers.
4. Click Save.

Result: documents of the type use that mode from their next posting. Refused for anyone but an administrator.

API: `POST /config/document-type-from`, `POST /config/document-type-to`, column `costLayers`.

| Value | Screen name | What the side does | Used on, in the base pack |
|-------|-------------|--------------------|---------------------------|
| 0 | None | Nothing | Offers, contracts |
| 1 | Take (oldest first) | Takes its oldest layers | The from side of sales and transfers |
| 2 | Make (a purchase) | Makes a layer at the product value | The to side of purchase bills |
| 3 | Receive (a transfer) | Receives what the from side took, with its cost and date | The to side of transfers |
| 4 | Put back (a reversal) | Puts back what the reversed line took, latest first | The to side of sale reversals |
| 5 | Take off (a reversal) | Takes off the reversed line's own layer | The from side of purchase reversals |
| 6 | Make from used | Makes a layer at what the raw materials cost | The to side of Production |
| 7 | Receive converted | Receives what was taken, converted, each layer on its own | The to side of Unit split |
| 8 | Receive shared by average cost | Receives what was taken, its cost shared among the lines that come back or are lost, by quantity × average cost (`CostLayerBook.ShareTaken`) | The to side of Disassembly |

## The rules

- Layers are per holder. A cost centre gives up its oldest layers first.
- A transfer carries the layers it took, with their cost and purchase date.
- Stock comes in only with a cost. A purchase bill makes the layer; everything else brings layers that exist.
- Nothing goes out that the company does not hold.
- A production shares what its raw materials cost among its products, by quantity. Waste is not part of it.
- A disassembly shares what the product cost among what comes back and what is lost, by quantity × the ingredient's average cost. What is lost moves no stock; its share is an expense. See [Disassembly](disassembly.md).
- A sale's cost is what its layers held. The product value is credited with that cost, when the document type's product value amount is the line's stock cost.

## Below zero

Where a cost centre may go negative, the part it lacks is valued at the oldest cost the company holds elsewhere. That part is a layer below zero, left owing at the cost centre. The other cost centres' layers are not touched.

What comes in next at that cost centre settles the owed layer first. If its real cost differs, the difference goes into the product's value: a credit when the real cost was higher, a debit when lower.

## Refused when

| Message | Meaning |
|---------|---------|
| "the company holds … of …, … are needed." | The company's holders together have too little |
| "… comes in only with a purchase bill or from another place of the company." | A Receive side has nothing taken by the from side |
| "the side makes cost layers but names no product value for them." | A Make side has no product value configured |
| "… from the reversed line are still at the place, … are reversed; what was sold or moved is reversed first." | A purchase or production reversal, and part of its layer has gone |
| "the reversed line took … of … from the stock, … are reversed." | A sale reversal for more than the line took |
| "a unit split turns … in one other unit into this one; …" | A unit split with lines in several other units |
| "no conversion between the units of …." | A unit split with no conversion row |

## Related

- [Negative stock](negative-stock.md)
- [Orders to produce](production.md)
- [Disassembly](disassembly.md)
- [Convert stock between units](convert-stock.md)
- [Recipes](recipes.md)

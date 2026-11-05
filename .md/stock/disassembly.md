# Disassembly

Take a product apart into the ingredients of its recipe. What comes back goes into stock; what is lost goes to the waste expense. Each disassembly is a posted financial document, so stock and the books move together.

**Where:** Stock and production › Orders to produce, tab Disassemble. The documents made are listed under Documents › Production. **Who:** the Orders to produce screen in your menu, and the Disassembly document type given to you.

The base pack gives the document type Disassembly (code `DISASSEMBLY`, in the Production category), from a cost centre to a cost centre, with the line types Product taken apart, Recovered component and Disassembly waste. It has no reversal type.

There is no separate disassembly recipe. The product's recipe is read backwards. Each recipe row's Recovered percent says how much of the ingredient comes back; the rest is waste. The recipe's Waste percent (waste in making) is not used here. See [Recipes](recipes.md).

## Actions

### Work out what comes back

Steps:
1. On the Disassemble tab, choose Product, Unit and Quantity.
2. Choose Taken apart at, the cost centre.
3. Choose the Date. Empty means today.
4. Click "Work out what comes back".

Result: a table of ingredients. Nothing is saved. For each row:
- Comes back: the recipe's quantity × the quantity taken apart × Recovered percent ÷ 100.
- Lost (waste): the rest of the recipe's quantity × the quantity.
- Held here: what the cost centre holds of the ingredient.

A product with no recipe shows "The product has no recipe: nothing to take apart.", and Disassemble cannot be pressed.

API: `GET /production/disassemble/calculate?product={id}&unit={id}&quantity={n}&costCenter={id}`

### Disassemble

Steps:
1. Work out what comes back.
2. Change Comes back or Lost (waste) of a row if the real result differed.
3. Click Disassemble.

Result:
- A Disassembly document is made, numbered and posted, and the screen opens it.
- One Product taken apart line. One Recovered component line per ingredient that comes back. One Disassembly waste line per ingredient with a loss.
- The product leaves the cost centre at its cost, oldest layers first.
- That cost is shared among all the lines that come back or are lost. See [How it is valued](#how-it-is-valued).
- What comes back enters stock at the same cost centre, at its share, as a new cost layer.
- What is lost moves no stock. Its share goes to the waste expense.
- The accounts move as the document type's posting scenarios say.

Refused when:
- No cost centre: "Choose where it is taken apart."
- The quantity is not above 0.
- The product has no recipe for this unit and no lines are given: NotConfiguredError, "the product has no recipe for this unit; give what comes back."
- A line names the product itself, has a negative number, or has both numbers 0: "An ingredient is another product; what comes back and what is lost are 0 or more, and not both 0."
- The cost centre does not hold the product: the normal stock check, see [negative stock](negative-stock.md).
- A closed period, or any other rule of saving and posting a document.

API: `POST /production/disassemble` with `costCenterId`, `productId`, `unitId`, `quantity`, and optionally `date`, `note`, `lines` (each `productId`, `unitId`, `recovered`, `lost`), `toCostCenterId` and `typeId`. The screen does not send `note`, `toCostCenterId` or `typeId`.

### A disassembly is not reversed

A disassembly is not reversed, like a production. The starter packs give no reversal type for it, so the Reverse button does not show and the server refuses with `NotConfiguredError`. A disassembly entered by mistake is corrected by producing the product again: see [Orders to produce](production.md).

## How it is valued

1. The product leaves at its cost, first in, first out.
2. That cost is shared among every line that comes back or is lost, in proportion to quantity × the ingredient's current average cost.
3. The average cost is the average of what the cost centre holds. Else of what the company holds. Else the ingredient's last known cost. If no ingredient has any cost, the cost is shared by quantity.

Postings in the starter packs:

| Line | Debit | Credit |
|------|-------|--------|
| Product taken apart | Production account (role production, e.g. 331) | Stock |
| Recovered component | Stock | Production account |
| Disassembly waste | Waste expense (role waste, e.g. 687) | Production account |

The production account nets to zero, and the document balances.

Example: a shelf is 2 boards (Recovered percent 100, boards cost 10 each) and 8 screws (Recovered percent 50, screws cost 1 each). One shelf cost 28. Taking 1 shelf apart:
- Boards: 2 come back, worth 20.
- Screws: 4 come back, worth 4. 4 are lost, worth 4.
- Stock goes up by 20 + 4, the waste expense by 4, and the shelf leaves at 28.

After **Work out what comes back**, the tab shows how much of the product the place holds (`GET /api/v1/production/held?costCenter=&product=&unit=`).

## Related

- [Orders to produce](production.md)
- [Recipes](recipes.md)
- [Cost layers](cost-layers.md)
- [Production documents](../documents/categories/production.md)
- [Reversing a document](../documents/reversing.md)

# Production documents

Raw materials used, products made, waste. The product takes the cost of what was used. A disassembly does the opposite: a product taken apart into its ingredients.

**Where:** Documents › Production. **Who:** see [who may do what](../life-cycle.md#who-may-do-what). Screens that also make these documents, Orders to produce, Convert stock between units and the Production point display, are in Stock and production and Point of sale.

## Types in the base pack

| Type | Code | From | To | Kinds of line | Partial reversal | Reversed by |
|------|------|------|----|---------------|------------------|-------------|
| Production | `PRODUCTION` | Cost centre | Cost centre | Raw material used, Produced item, Waste | No | Not reversed |
| Disassembly | `DISASSEMBLY` | Cost centre | Cost centre | Product taken apart, Recovered component, Disassembly waste | No | Not reversed |
| Unit split | `UNIT_SPLIT` | Cost centre | Cost centre | Raw material used, Produced item | Yes | Reversal of unit split |

| Kind | `stock_direction` | What it does |
|------|-------------------|--------------|
| Raw material used | 2 | Out of the from side only |
| Produced item | 3 | Into the to side only |
| Waste | 4 | Out of the from side only, not part of what is made |
| Product taken apart | 2 | Out of the from side only |
| Recovered component | 3 | Into the to side only |
| Disassembly waste | 5 | Value only: moves no stock, carries its share of the cost |

The Disassembly number codes in the packs: AL/XK `FCM`, DE/AT `DEM`, FR/BE/LU `DMT`, CZ/SK `DEM`, ES/PT `DSM`, IT `SMT`, RO `DZA`, MK `ДЕМ`, GR/CY `ΑΠΣ`, others `DIS`.

## Actions

### Write a production

Steps:
1. Press **New**, then Production.
2. Pick the place in **From** and in **To**.
3. Add Raw material used, Produced item and Waste lines.
4. Save and post.

Result: raw materials leave at their oldest cost. The products get that cost, shared by quantity among them, as a new cost layer. Waste leaves the same way, but its cost is an expense, not part of what is made.

Refused when:
- `StockError`: not enough raw material: see [stock checks](../lines.md#stock-and-cost-checks).
- `StockError`: {item} tracks lots and can't go through production or a stock conversion.
- Otherwise see [saving](../drafts.md#save) and [posting](../posting.md#post).

API: `POST documents`, `POST documents/{id}/post`.

### Split into another unit

Steps:
1. Press **New**, then Unit split.
2. Add a Raw material used line: the product in its unit.
3. Add a Produced item line: the same product in the new unit, with the converted quantity.
4. Save and post.

Result: the same stock in another unit. Each cost layer keeps its value, so the unit cost changes by the same factor. The factor comes from Products and services › Unit conversions, the product's own first, either way round.

Refused when:
- `NotConfiguredError`: no conversion between the units of {item}.
- `StockError`: x of {item} make y in this unit, not z. The quantity must be exactly the converted one.
- `StockError`: a unit split turns {item} in one other unit into this one; the document has n lines of it in other units.

### Take a product apart

Steps:
1. Use Stock and production › Orders to produce, tab Disassemble. See [Disassembly](../../stock/disassembly.md).
2. Or press **New**, then Disassembly, and add a Product taken apart line, Recovered component lines and Disassembly waste lines. Save and post.

Result: the product leaves the from side at its oldest cost. That cost is shared among the Recovered component and Disassembly waste lines, by quantity × the ingredient's average cost. Recovered components come into the to side as a new cost layer (cost-layer mode 8, Receive shared by average cost). Waste moves no stock; its share goes to the waste expense.

Refused when: as in [Disassembly](../../stock/disassembly.md).

API: `POST /production/disassemble`, or `POST documents`, `POST documents/{id}/post`.

### A production is not reversed

A production is not reversed: making a product cannot be undone, and the order it was made for stays produced. The starter packs give no reversal type for it, so the Reverse button does not show and the server refuses with `NotConfiguredError`. A production entered by mistake is corrected with a Disassembly: see [Take a product apart](#take-a-product-apart). A disassembly is not reversed either, for the same reason; a mistaken one is corrected by producing again.

## Related

- [Lines](../lines.md)
- [Reversals](reversals.md)
- [Disassembly](../../stock/disassembly.md)
- [Orders to produce](../../stock/production.md)

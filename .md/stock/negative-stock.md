# Negative stock

Whether a holder's stock may go below zero. It is one company setting for each kind of holder: cost centres, partners and employees. It is configuration, read from the company's own row.

**Where:** Company › Cost centres, open the company, Properties. **Who:** the write right on the company's cost centre category.

The base pack gives the Company cost-centre type three Number properties:

| Property | Holder |
|----------|--------|
| Negative stock allowed (cost centres) | Warehouses, shops and other cost centres |
| Negative stock allowed (partners) | Clients and suppliers |
| Negative stock allowed (employees) | Staff |

## Actions

### Set the rule

Steps:
1. Open Company › Cost centres and click the company.
2. In Properties, give each one a value: 0 means it may not go negative; 1 or empty means it may.
3. Click Save.

Result: the next save or posting of a document reads the new value. Stock already below zero stays as it is.

API: `POST /cost-centers` with the property's value in `values`.

### See what is below zero

Steps: open Reports › Stock and sales › Stock.

Result: the figure Below zero counts the lines under zero.

API: `GET /reports/stock`

## The two stock checks

### Per holder: the setting

Runs when a document is saved, when its posting preview is opened and when it is posted.

When the setting is 0 and a line would take the holder below zero, it is refused, naming the other holders that have enough:

"there are 2 of Flour, 5 are needed; take them from Main warehouse (20)."

With none: "… no other place has them."

The line can then take it from one of those.

### Across the company: always

Runs on sides that take cost layers, whatever the setting says. Nothing goes out that the company does not hold:

"the company holds 3 of Flour, 5 are needed."

So with the setting at 1, a cost centre may go below zero only while the company's other cost centres hold the rest. The missing part is valued at the oldest cost held elsewhere and left owing at the cost centre. See [cost layers](cost-layers.md).

## Empty is allowed

Empty means the stock may go negative. No pack file or setup code fills these values, so a new company starts with all three empty.

## How the rule is found

The engine looks for a Number property of the Company type with the Code `NEGATIVE_STOCK` that points at the side's entity (the property's Elements link). All three properties carry this code; the Elements link tells them apart. The name is not read, so the properties can be renamed and translated freely. The starter packs set the code. See [Property codes the system reads](../system-settings/types-and-categories.md#property-codes-the-system-reads).

## Lots and serial numbers

These have their own checks at posting, whatever this setting says. See [lots](lots.md) and [serial numbers](serial-numbers.md).

## Related

- [Cost centres](../company/cost-centres.md)
- [Cost layers](cost-layers.md)
- [Orders to produce](production.md)
- [Convert stock between units](convert-stock.md)

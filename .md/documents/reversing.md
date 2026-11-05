# Reversing a posted document

A posted document never changes. It is corrected by a reversal: a new document, its sides swapped, linked to the original and posted at once.

**Where:** a posted document › **Generate** › **Reverse…**, shown only when a document type exists that reverses this type (the form says `hasReversal`). **Who:** write on the document's category, and the reversal type given to you with write on its category (Reversals in the base pack).

## Actions

### Reverse a whole document

Steps:
1. Open the posted document.
2. Press **Generate**, then **Reverse…**
3. Set the posting date. Today is proposed.
4. Press **Reverse**.

Result:
- A new document of the type's reversal type: a copy with from and to swapped, on the document and on each line, debit and credit swapped. Each line points at its original line.
- It is created from the original, carries its number ("Reversal of {number}"), and is posted through its own configuration.
- The original stays as it was. A reversal of a sale puts stock back on the layers it took; a reversal of a purchase takes off what the line brought.
- An invoice's open amount falls by the reversal's total. A reversed payment opens its invoices again.
- A company's draft purchase made from a group's sale follows it: what is left of the sale, or nothing.
- The screen opens the reversal, read only.

Refused when:

| Message | Meaning |
|---------|---------|
| `ReversalError`: only a posted document is reversed; an unposted one is changed. | |
| `NotConfiguredError`: no reversal type reverses this document type; set one first. | System settings › Document types › a reversal type › General › It reverses. See each [category](categories/README.md) for which types have one |
| `ReversalError`: nothing is left to reverse. | Earlier reversals took it all |
| `NotAllowedError`: this document type has not been given to you… | The reversal type was not given |
| `PeriodClosedError` | The reversal's posting date is in a closed period |
| `ReversalError`: a later depreciation run stands on this one; undo the later run first. | Depreciation journals only |
| `StockError`: x of {item} from the reversed line are still at the place, y are reversed; … | A purchase's goods have already left |
| `StockError`: the reversed line took x of {item} from the stock, y are reversed. | |
| Any posting refusal | See [posting.md](posting.md) |

API: `POST documents/{id}/reverse` with `{ "postingDate": "2026-11-02", "quantities": null, "serials": null }`. The answer is the posted reversal.

### Reverse part of a document

Only where the type allows it: "A reversal may take part of the lines" on the type's General tab. In the base pack: sales invoices, purchase invoices, point-of-sale receipts, unit split. A production is never reversed.

Steps:
1. In the Reverse dialog, each line shows **Quantity** (the line's own), **Left** (what earlier reversals have not taken yet) and a **Reverse** box, filled with what is left. A line with nothing left is not shown. On a line with serial numbers, only the numbers not yet back are offered.
2. Change the box of each line to reverse in part. Type 0 to leave a line out. Leaving every box as shown reverses all that is left.
3. On a line with serial numbers, untick the numbers that do not come back. The quantity follows the ticks.
4. Press **Reverse**.

Result: as above, for those quantities, debit and credit shared in proportion. A line whose box was changed reverses the typed quantity. A line left untouched reverses what is left of it. A line set to 0 is not reversed. It may be done again for what is left.

If what is left can't be read, the dialog shows the lines as they are on the document.

Refused when:
- `ReversalError`: documents of this type are only reversed whole.
- `ReversalError`: line n has x left to reverse.
- `ReversalError`: line n reverses x: name that many of its serial numbers.
- `ReversalError`: a line to reverse is not a line of the document.

API: as above, with `quantities` as line id → quantity and `serials` as line id → list of numbers. A line not named in `quantities` reverses what is left of it; a line named with 0 is left out. What is left: `GET documents/{id}/reversible` gives, per line, `{ id, left, serials }`: the quantity not reversed yet and the serial numbers not yet back. It reads nothing for a document you may not read.

### Reverse a sheet

Steps:
1. Open the posted sheet, then **Generate › Reverse…**
2. Set the posting date and press **Reverse**.

Result: a sheet of its reversal type holds the reversal of each journal, posted together, all or nothing. A sheet is reversed only whole. A reversal sheet is filed in Reversals with its journals.

A reversed depreciation journal cancels its run: the assets go back to their book value before it.

API: as above: a document with journals under it reverses as a sheet, and `quantities` is ignored.

## Not possible

- A reversal is never written by hand. Saving a document of a reversal type gives `ReversalError: a reversal is made only by reversing the document it reverses.`
- A reversal is not reversed: the Generate menu leaves it out, and it can't be repeated.
- A draft is not reversed: change it or switch it off.

## Related

- [The Reversals category](categories/reversals.md)
- [Posting](posting.md)
- [Payments and open invoices](payments-and-open-invoices.md)

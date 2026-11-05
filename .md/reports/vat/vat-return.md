# VAT return

The VAT of a period per VAT group: the sales book against the purchase book, and what is owed. This is the summary; the country's form is [vat-return-by-boxes.md](vat-return-by-boxes.md).

**Where:** Reports › VAT › VAT return. **Who:** the `vat-return` report in your menu.

## Actions

### Show the VAT return

Filters: **Sales (output VAT)** (one or more categories), **Purchases (input VAT)** (one or more), **From**, **Up to**. Either side may be left empty, but not both.

Steps:
1. Choose the categories of each side, set the period and press **Show**.

Shows:
- Figures: Output VAT, Input VAT, and VAT payable or VAT to be refunded.
- Table: Side, VAT group, Rate %, Base, VAT.

Each side is worked out as in VAT books, reversals negative. A side with no categories counts as nothing.

Result: nothing changes.

Refused when:
- "Name at least one document category."
- "The period ends before it starts."

API: `GET /reports/vat-return?output=&input=&from=&to=`, with `output` and `input` repeated per category. Excel: `/reports/vat-return/xlsx`. PDF: `/reports/vat-return/pdf`, same parameters.

## Related

- [vat-books.md](vat-books.md)
- [vat-return-by-boxes.md](vat-return-by-boxes.md)

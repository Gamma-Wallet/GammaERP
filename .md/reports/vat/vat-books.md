# VAT books

The VAT register: the sales book or the purchase book for a period. Which documents make a book is your choice of document categories.

**Where:** Reports › VAT › VAT books. **Who:** the `vat-register` report in your menu.

## Actions

### Show a VAT book

Filters: **Document categories** (required, one or more), **From**, **Up to**.

Steps:
1. Choose the categories, such as Sales for the sales book. Set the period and press **Show**.

Shows:
- Figures: Taxable base, VAT, then the VAT of each VAT group with its base.
- Table: Date, Document, Their number, Type, Partner, Tax number, City, Cost centre, Base, VAT, Total.

Tax number and City come from the partner properties with the Code `TAX_NUMBER` and `CITY`, not from their names, so they can be renamed and translated. See [Property codes the system reads](../../system-settings/types-and-categories.md#property-codes-the-system-reads).

The book holds the posted documents of those categories in the period, and the reversals of their types, which count negative.

Result: nothing changes. Clicking a row opens the document.

Refused when:
- "Name at least one document category."
- "The period ends before it starts."

API: `GET /reports/vat-register?category=&category=&from=&to=`. Excel: `/reports/vat-register/xlsx`. PDF: `/reports/vat-register/pdf`, same parameters.

## Related

- [vat-return.md](vat-return.md)
- [../../accounting/vat-groups.md](../../accounting/vat-groups.md)

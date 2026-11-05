# VAT return by boxes

The country's VAT return form for a period, box by box, from the company's boxes and their rules.

**Where:** Reports › VAT › VAT return by boxes. **Who:** the `vat-return-form` report in your menu.

## Actions

### Show the form

Filters: **From**, **Up to**.

Steps:
1. Set the period and press **Show**.

Shows:
- Figures: Output VAT (sales boxes holding VAT), Input VAT (purchase boxes holding VAT), and VAT payable or VAT to be refunded.
- Table: Box, Name, Side, Value, in the boxes' order. A box with no rules, or no lines, stays 0.

Each posted line with a VAT group goes into the box of the most specific rule that takes it, per side and amount. A reversal counts negative.

Result: nothing changes.

Refused when: "The period ends before it starts."

API: `GET /reports/vat-return/form?from=&to=`.

## Related

- [../../accounting/vat-return-boxes.md](../../accounting/vat-return-boxes.md)
- [vat-return.md](vat-return.md)

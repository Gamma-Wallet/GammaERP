# VAT return boxes and rules

The boxes of the country's VAT return form, and the rules saying which document lines fill each box. Nothing about a country is in the code.

**Where:** Accounting setup › VAT return boxes (`/config/vat-return-boxes`, table `tbl_vat_return_box`) and Accounting setup › VAT return box rules (`/config/vat-return-box-rules`, table `tbl_vat_return_box_rule`). **Who:** an administrator changes; see [configuration-lists.md](configuration-lists.md).

## Box columns

| Column | Meaning |
|--------|---------|
| Code | The box's number on the form. |
| Name | Its name, in the country's language. |
| Sort order | Its place on the report. |
| Is input | Ticked: purchases (input VAT). Not ticked: sales (output VAT). |
| Amount | 1 the taxable value, 2 the VAT. |

A box with no rules stays 0.

## Rule columns

| Column | Meaning |
|--------|---------|
| Box | The box it fills. |
| Document category | Only documents of this category. Empty: any. |
| Document type | Only this type. Empty: any. |
| Transaction type | Only this line type. Empty: any. |
| VAT group | Only this group. Empty: any. |

What a rule puts in its box, taxable value or VAT, is the box's Amount.

## How a line finds its box

For each posted line with a VAT group:

1. The rules that take it are grouped by side and amount.
2. In each group the most specific rule wins: a document type counts 4, a transaction type 2, a category 1 and a VAT group 1.
3. The line's taxable value or VAT goes in that rule's box.
4. A reversal type counts as the type it reverses, with a negative sign.

So a customs declaration's lines can go to the import boxes and not to the purchases ones.

## Actions

### Add or change a box

Steps:
1. Press **Add**, or click a box.
2. Fill in the columns and press **Save**.

Result: the box shows on VAT return by boxes.

Refused when: "A box holds the taxable value (1) or the VAT (2)." Also see [configuration-lists.md](configuration-lists.md).

API: `POST /config/vat-return-boxes`.

### Add or change a rule

Steps:
1. Press **Add**, or click a rule.
2. Choose the box and the parts that narrow it. Press **Save**.

Result: the next report uses it. Posted documents do not change.

API: `POST /config/vat-return-box-rules`.

### Switch a box or rule off

Steps: open it, press **Switch off…** and confirm.

API: `DELETE /config/vat-return-boxes/{id}`, `DELETE /config/vat-return-box-rules/{id}`.

### Export, template and import

Migration steps `VAT_RETURN_BOXES` and `VAT_RETURN_BOX_RULES`. See [configuration-lists.md](configuration-lists.md).

**Amount** on the box form is chosen by name: Taxable value (1) or VAT (2).

## Related

- [../reports/vat/vat-return-by-boxes.md](../reports/vat/vat-return-by-boxes.md)
- [vat-groups.md](vat-groups.md)

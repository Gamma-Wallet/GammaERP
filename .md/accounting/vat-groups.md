# VAT groups

The VAT groups of the company, each with its rate. Lines carry a VAT group, and the VAT reports sum by it.

**Where:** Accounting setup › VAT groups (`/config/vat-groups`, table `tbl_vat_group`). **Who:** an administrator changes; see [configuration-lists.md](configuration-lists.md).

## Columns

| Column | Meaning |
|--------|---------|
| Code, Name, Description | What people read. |
| Rate | The percentage. |
| Depreciation | The group depreciation journals put on their lines. The journal takes the company's own group with this ticked. |

## Where the groups are offered

On a document type's **VAT groups** tab (`document-type-vat-groups`), under System settings › Document types. A type naming none offers the company's own groups, the highest rate first.

## Actions

### Add or change a VAT group

Steps:
1. Press **Add**, or click a group.
2. Fill in the columns and press **Save**.

Result: new lines can use it. Posted documents keep their amounts.

Refused when: see [configuration-lists.md](configuration-lists.md).

API: `POST /config/vat-groups`.

### Switch a VAT group off

Steps: open the group, press **Switch off…** and confirm.

Result: it is no longer offered or read. Nothing is deleted.

API: `DELETE /config/vat-groups/{id}`.

### Export, template and import

Migration step `VAT_GROUPS`. See [configuration-lists.md](configuration-lists.md).

## Related

- [vat-return-boxes.md](vat-return-boxes.md)
- [../reports/vat/vat-books.md](../reports/vat/vat-books.md)
- [../fixed-assets/depreciation-run.md](../fixed-assets/depreciation-run.md)

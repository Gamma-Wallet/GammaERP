# Account mapping

Which account of another chart each statutory account is posted on, such as the group's chart or an IFRS chart. Posting writes the statutory entries, then the same entries again on the mapped accounts.

**Where:** Accounting setup › Account mapping. **Who:** anyone with it in their menu reads. An administrator changes rows. The screen's generic actions are in [Configuration lists](configuration-lists.md).

Configuration: `tbl_inter_chart_chart`, model `chart-mappings`. Rows are the company's own.

| Field | Column | Meaning |
|---|---|---|
| Chart1 | `Chart1Id` | The statutory account. |
| Chart2 | `Chart2Id` | The account of the other chart it is posted on. |

## How it works

- A chart is posted to once the company maps an account to it. The other chart is the one whose category is not statutory. An administrator marks the statutory category on its form: see [Mark the statutory chart](chart-setup.md#mark-the-statutory-chart).
- From then on, every statutory account a posting touches must be mapped there exactly once. Many statutory accounts may map to one account.
- The amounts are the statutory ones, in full. The ownership share applies only at consolidation.
- A mapped entry never goes to a group account.

## Actions

### Map an account

Steps:

1. Press **Add**, or press a row.
2. Choose Chart1, the statutory account, and Chart2, the account of the other chart.
3. Press **Save**.

Result: postings from now on also write the entry on the Chart2 account. Documents already posted do not change.

Refused when: as in [Configuration lists](configuration-lists.md).

API: `GET /config/chart-mappings`, `POST /config/chart-mappings`

### Remove a mapping

Steps:

1. Press the row, then **Switch off...**
2. Confirm.

Result: the row stops counting. If the chart still has other mappings, a posting that touches that statutory account is refused with `MappingError`.

API: `DELETE /config/chart-mappings/{id}`

## What refuses a posting

- `MappingError: in chart {c}, {code} has no account.` A statutory account posted has no mapping in a chart the company posts to. Add the row.
- `MappingError: in chart {c}, {code} has more than one.` Two active rows map it in the same chart. Switch one off.

## Related

- [Chart of accounts](chart-of-accounts.md)
- [Chart setup](chart-setup.md)
- [Posting](../documents/posting.md)

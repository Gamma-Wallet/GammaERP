# Dashboard items

What the dashboard shows, per company. The engine only knows how to work out each kind; everything else is here.

**Where:** Reports › Dashboard items (`/config/dashboard-items`, table `tbl_dashboard_item`). **Who:** an administrator changes; see [configuration-lists.md](../accounting/configuration-lists.md).

## Columns

| Column | Meaning |
|--------|---------|
| Code, Name | What people read. |
| Kind | How it is worked out, see below. |
| Document category | The documents it counts. On a balance, the documents its report opens with. |
| Second category | The documents a chart sets against the first, or that a count adds to them. |
| Partner category | The partners of a balance. |
| Negate | The other way round: a balance shows with the other sign, a total counts a rise as bad, Overdue shows what is owed to suppliers. |
| Sort order | The order on the dashboard. |

## Kinds

| Kind | Shown as | What it shows |
|------|----------|---------------|
| Total | Card | Net value of the category in the period, against the period before |
| Balance | Card | What the partner category stood at on the last day, debit less credit |
| Drafts | Card | Documents not posted, of its categories, or of all with none named |
| Overdue | Card | What clients owe past its due date; with Negate, what is owed to suppliers |
| GrossProfit | Card | Net value less cost of the lines, against the period before |
| Monthly | Chart | Net value by month, for twelve months, with the second category beside it |
| MonthlyVat | Chart | VAT by month, the same way |
| GrossProfitByCostCenter | Chart | Gross profit by cost centre, the top ten |
| TopPartners | List | Top ten partners by net value |
| TopProducts | List | Top ten products by net value |
| ByCostCenter | List | Top ten cost centres by net value |
| TopCombined | List | Top ten composite products by net value |

The starter pack has sixteen items, such as Sales, Purchases, Clients owe us, We owe suppliers, Drafts waiting, Top clients and Gross profit.

## Actions

### Add or change an item

Steps:
1. Press **Add**, or click an item.
2. Enter **Kind** exactly as above. Fill in what that kind reads. Press **Save**.

Result: the dashboard shows it from the next load.

Refused when:
- "The dashboard can work out Total, Balance, ...".
- "A balance item needs a client and supplier category." A Balance item with no Partner category.
- "This kind of item needs a document category." Every kind but Balance, Drafts and Overdue needs a Document category.

Also see [configuration-lists.md](../accounting/configuration-lists.md).

API: `POST /config/dashboard-items`.

### Switch an item off

Steps: open it, press **Switch off…** and confirm.

Result: the dashboard leaves it out.

API: `DELETE /config/dashboard-items/{id}`.

## Related

- [dashboard.md](dashboard.md)
- [../accounting/configuration-lists.md](../accounting/configuration-lists.md)

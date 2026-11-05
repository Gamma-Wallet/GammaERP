# Dashboard

The cards, charts and top lists the company configured, each opening the report behind it. Amounts are from posted documents, net of VAT, reversals subtracted, in the company's currency.

**Where:** Dashboard. **Who:** the Dashboard item in your menu. Administrators pass.

## Actions

### See the dashboard

Steps:
1. Open it. It loads at once for this month so far.
2. Change **From**, **Up to** or **Cost centre**. It reloads by itself.
3. Press **Refresh** to read it again.

Result: nothing changes.

- A card with a total shows the change from the period before.
- A balance or overdue card shows the amount at the **Up to** day.
- A drafts card counts documents not posted yet.
- A chart shows the twelve months up to the end of the period.
- A top list shows up to ten rows.

If no item is configured, the screen says so and points to Reports, Dashboard items.

The period before: from the first of a month, the same stretch of months before, so this month so far meets last month up to the same day. Any other period: the same number of days just before it.

**Cost centre** narrows the items read from document lines. Balance, Drafts and Overdue do not take it.

Refused when:
- `NotAllowedError`: "the dashboard is not in your menu."
- "The period ends before it starts."

API: `GET /dashboard?from=&to=&costCenter=`.

### Open the report behind an item

Steps:
1. Click a card, or **Open the report** under a chart or list.

Result: the report opens at once with the same period.

| Kind | Opens |
|------|-------|
| Total | Document totals, or Sales and purchases by line with a cost centre chosen |
| Balance | Client statistics |
| Drafts | The category's document list |
| Overdue | Open invoices |
| MonthlyVat | VAT books |
| TopPartners | Sales and purchases by client |
| ByCostCenter, GrossProfitByCostCenter | Sales and purchases by cost centre |
| Others | Sales and purchases by line |

An item with no document category opens nothing.

## Related

- [dashboard-items.md](dashboard-items.md)
- [README.md](README.md)

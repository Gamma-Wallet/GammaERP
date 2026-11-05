# Ageing

What each client or supplier has open at a day, by how late it is.

**Where:** Reports › Financial › Ageing. **Who:** the `ageing` report in your menu.

## Actions

### Show the ageing

Filters: **At**, a day. It opens at today.

Steps:
1. Set **At** and press **Show**.

Shows:
- Figures: Clients owe, Owed to suppliers.
- Table: Client or supplier, Side, Not due, 1–30 days, 31–60 days, 61–90 days, Over 90 days, Total.

Result: nothing changes. It sums the rows of Open invoices at that day, so the same rules apply. Clicking a row opens Open invoices at the same day, filtered on that row's partner. The partner filter stays set there and can be changed.

API: `GET /reports/ageing?to=`.

## Related

- [open-invoices.md](open-invoices.md)
- [../dashboard.md](../dashboard.md)

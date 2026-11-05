# Forecast

What the pipeline may bring, per month and currency.

**Where:** CRM › Forecast. **Who:** the Forecast report in your menu.

## Actions

### Read the forecast

Steps: open the report. It has no filters.

Result: one row per month of expected close and per currency, oldest month first:

| Column | Holds |
|--------|-------|
| Month | Year and month of the expected close |
| Currency | The opportunity's currency |
| Open amount | The amounts of opportunities still open |
| Weighted by chance | Each open amount × its stage's chance ÷ 100 |
| Won | The amounts of opportunities at a won stage, in full |
| Invoiced | What the month's opportunities have invoiced so far, in their currency at each document's day. An invoice here is a posted document whose type holds an open item, the invoice run's rule; a posted reversal of one counts against it. |

Only the opportunities the Opportunities register shows you are counted: its category rights and its Access tab apply. Administrators see all. Lost opportunities are left out. So are opportunities with no expected close, and those on a switched-off stage. Currencies are never added together.

Refused when: the report is not in your menu: "this screen is not in your menu."

API: `GET /opportunities/forecast`

## Example

Stage Propose has a chance of 40. An opportunity of 1000 EUR there, closing in March, adds 1000 to Open amount and 400 to Weighted by chance. Moved to Won, it adds 1000 to Won and nothing to the other two.

**Excel** downloads the forecast as a sheet (`GET /api/v1/opportunities/forecast/xlsx`). It has no PDF.

## Related

- [Pipeline](pipeline.md)
- [Opportunity stages](opportunity-stages.md)
- [Opportunities](opportunities.md)

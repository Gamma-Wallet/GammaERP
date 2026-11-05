# Home and dashboard

The frame around every screen, the first page, and the dashboard.

**Where:** the logo at the top left opens Home; Dashboard in the side menu. **Who:** Home: any user working at a cost centre. Dashboard: the `dashboard` screen in your menu.

## The frame

- **Top bar:** the logo, the company's name, the language list, the Files button (when Files is in your menu), Help (not active yet) and the Account menu.
- **Account menu:** your name and user name, Change cost centre, Change password, Sign out.
- **Side menu:** your name, your cost centre, then your menu tree. Folders open and close, and this browser remembers which ones.
- A document category with several document types opens as a folder: **All**, then one link per type.
- With nothing given to you, the menu says "Nothing has been given to you yet. Ask an administrator for access."

An address that the menu does not hold goes to Home, for the screens that check it.

## Actions

### Use the home page

Steps:

1. Press the logo.
2. Each top folder of your menu is a card with its items as links. Press one.

Result: the screen opens.

Two cards can show on top, for administrators only:

- **Finish your company:** "Add its tax number, trade name, address, phone and logo". **Open the company** goes to Company › Cost centres. **Later** puts the card away for this user in this browser.
- **Set up the group's books:** see [Add a company to a group](add-a-company-to-a-group.md).

API: `GET /menu`, `GET /group-books`

### Read the dashboard

Steps:

1. Open **Dashboard**.
2. Set From, Up to and Cost centre. The period starts as this month.
3. The dashboard loads by itself on each change. **Refresh** reads it again.
4. Press a card, or **Open the report** under a chart or list, to open the report behind it.

Result: nothing changes. It reads posted documents, net of VAT, with reversals subtracted, in the company's base currency. Each item comes from the same lines as its report.

What shows is configuration: the rows of `tbl_dashboard_item`, in Reports › Dashboard items.

| Kind | Shows as | Works out |
|---|---|---|
| Total | Card | The net total of a document category, against the period before |
| Balance | Card | What a partner category stands at on the last day |
| Drafts | Card | How many documents are not posted yet |
| Overdue | Card | What clients owe past due, or with Negate what is owed to suppliers |
| GrossProfit | Card | Value less cost, against the period before |
| Monthly | Chart | Twelve months of a category, with a second category if named |
| MonthlyVat | Chart | Twelve months of VAT |
| GrossProfitByCostCenter | Chart | Gross profit per cost centre |
| TopPartners, TopProducts, TopCombined, ByCostCenter | List | The top 10 rows |

The period before: from the first of a month, the same stretch of months earlier. Any other period, the same number of days just before it.

Refused when:

- `NotAllowedError: the dashboard is not in your menu.`
- "The period ends before it starts."

With no item configured the page says "No item is configured. Add them under Reports, Dashboard items."

API: `GET /dashboard?from=&to=&costCenter=`

## Related

- [Choose where you work](choose-where-you-work.md)
- [Language](language.md)
- [Menu access](../users-and-rights/menu-access.md)

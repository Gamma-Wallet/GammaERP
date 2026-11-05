# Close the year

Checks that a fiscal year is finished and locks it. Nothing is written to the books: balances run on across years and the statements work out each period.

**Where:** Accounting setup › Close the year. **Who:** the Close the year item in your menu. Administrators pass.

The fiscal year starts in the month set on the company's property "Fiscal year starts (month)", January when empty.

## Actions

### See where a year stands

Steps:
1. Open the screen. It starts on 31 December of last year.
2. Change **A day in the year** to see another year.

Result: nothing changes. The screen shows:
- the year's first and last day
- Documents not posted: drafts dated up to the year's end, leaving out types whose posting books nothing
- debit and credit of the year's trial balance
- whether the year is closed

API: `GET /year-closing?day=`.

### Close a year

Steps:
1. Choose a day in the year.
2. Press **Close the year**. The button is on only when no draft is left and debit equals credit.

Result: the company's posting lock moves to the year's last day. Nothing can be posted on a day up to it, reversals included. If the year is closed already, nothing changes.

Refused when:
- `PeriodClosedError`: "... documents are not posted yet in this year: post them or switch them off first."
- `PeriodClosedError`: "the books of this year do not agree: debit ... ≠ credit ...".
- "Configuration belongs to a company: choose one of its nodes."

API: `POST /year-closing?day=`.

### Close a year and change the company's currency

The company keeps its books in another currency from the next fiscal year's first day (the founder, 2026-11-04).

Steps:
1. Enter the new currency's exchange rate for the next year's first day under Currencies and exchange rates (for example 1 EUR = 97.50 ALL on 1 January).
2. Open Close the year and choose a day in the year to close.
3. In **Next year's currency** choose the new currency. The hint shows today's currency.
4. Press **Close the year**.

Result: the year is locked as above, and from the next year's first day the company's own currency is the new one (`currencies.tbl_company_currency` keeps the change: the day and the rate). Nothing is written to the books:
- What was posted up to the change stays as written. A report read on a day up to the change shows the old currency, unchanged.
- A report read on a day after it converts what was posted before: divided by the rate (97.50 ALL become 1.00 EUR). This counts for the trial balance, the account ledger, the balance sheet, profit and loss, the cost centre, client and supplier and staff balances and ledgers, client statistics (the partner ranking too, each row at its day's rate, see [client-statistics.md](../reports/stock-and-sales/client-statistics.md)), open invoices and ageing, and the dashboard.
- What is still in stock keeps its value: each open cost layer's value left and unit cost are converted once, here.
- The price lists (purchase, sale, sale by partner and value prices) are converted once: their company-currency value, and the value itself where it is in the old currency.

Refused when:
- as for Close a year above;
- "Enter the exchange rate of {currency} for {day} first: the books change currency at that day's rate." There is no rate of exactly that day;
- "The next year's currency is not one of the company's."

API: `POST /year-closing?day=&nextCurrency={currency id}`.

### Open a closed year again

An administrator moves the lock back under [closed-periods.md](closed-periods.md).

## Related

- [closed-periods.md](closed-periods.md)
- [../reports/financial/trial-balance.md](../reports/financial/trial-balance.md)

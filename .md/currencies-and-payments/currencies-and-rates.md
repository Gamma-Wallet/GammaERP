# Currencies and exchange rates

The company's currencies and their rates by day. A rate is one unit of the currency in the company's own currency. Amounts are kept in the document's currency and in the company's own (the `Lcu` amounts).

**Where:** Currencies and payments › Currencies and exchange rates. **Who:** the item in your menu to read. Changing currencies and exchange rates is for administrators only: the screen shows those buttons to them alone, and the server refuses anyone else with `NotAllowedError: only an administrator does this.`

## How a document takes a rate

A document in a foreign currency takes the latest rate on or before its day, when none is typed. The own currency's rate is always 1. With no rate, posting is refused: "A foreign currency needs its exchange rate: set it on the document and save it."

## Actions

### See a currency's rates

Steps:
1. Choose a currency on the left. Your own is marked **Own**.
2. Set **From** and **Up to**. The period opens at the last 90 days.

Result: nothing changes. The screen shows a chart, with two rates or more, and the rates by day.

API: `GET /currencies`, `GET /exchange-rates?currency=&from=&to=`. The rate a document of a day would take: `GET /exchange-rates/rate?currency=&date=`.

### Add a currency

Steps:
1. Press **New currency**.
2. Enter **Code**, three letters, and **Name**. An empty name takes the code.
3. Press **Add**.

Result: the currency is in the list, with no rates yet.

Refused when:
- "A currency code is three letters, such as EUR."
- "The company already has ...".
- "Currencies belong to a company: choose one of its nodes."

API: `POST /currencies` with `code` and `name`.

### Rename a currency

Steps:
1. Choose the currency, change **Name** and press **Rename**.

Result: the name changes, in English. A code stays what it is.

Refused when: "The company has no such currency."

API: `POST /currencies` with `id` and `name`.

### Switch a currency off

Steps:
1. Choose the currency and press **Switch off**. Your own currency has no such button.
2. Confirm.

Result: it leaves the list. Nothing is deleted.

Refused when:
- "The company's own currency can't be switched off."
- "... is used on a document or a price, so it stays."

API: `DELETE /currencies/{id}`.

### Set a rate

Steps:
1. Choose the currency.
2. Enter **Day** and **Rate**, 1 unit in the own currency.
3. Press **Set rate**.

Result: the day's rate is set. If the day has one, it changes and the old one stays as history. Documents already saved keep their rate.

Refused when:
- "A rate is above 0."
- "The base currency's rate is always 1."
- "The company has no such currency."
- "The company has no base currency."

API: `POST /exchange-rates` with `currencyId`, `rateDate`, `rate`.

The company's own currency is set when the company is made, and changes only from the start of a fiscal year: see [Close a year and change the company's currency](../accounting/year-closing.md#close-a-year-and-change-the-companys-currency).

## Open points

- Rates are typed by hand; the ERP fetches none. Fetching them is planned for the Gamma ERP MCP.

## Related

- [payment-methods.md](payment-methods.md)
- [../reports/README.md](../reports/README.md)

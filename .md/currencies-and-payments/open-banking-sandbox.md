# Trying open banking with the test banks

How to try every open banking step (connect a bank account, fetch its statement, pay by bank at the till) with Enable Banking's test banks, without a real bank account or real money. Done end to end on 2026-11-14 with the company Pizzeria Demo Napoli.

**Who:** an administrator of the company (the provider) and a user with the Bank statements screen and the point of sale.

## What it needs

| Need | Where |
|---|---|
| An Enable Banking account and a **sandbox** application | enablebanking.com, Control panel › API applications. Environment: Sandbox |
| The application's id and its `.pem` private key | Given when the application is registered. Keep the key out of every repository |
| The ERP's return address among the application's allowed addresses | e.g. `http://127.0.0.1:8090/bank-connected` for a local ERP. The sandbox accepts a local address |
| A company whose currency is euro | The payment QR code is euro only |

## 1. Give the company its provider

When the company is made (the New company page, step Open banking, or `create-company` with `--bank-provider EnableBanking --bank-app-id <id> --bank-key-file <app.pem> --bank-redirect <address> --bank-environment Sandbox`), or later in Currencies and payments › Open banking. See [Open banking](open-banking.md).

Check: the Open banking page shows "Set (fingerprint)" for the key and the banks "Sandbox".

## 2. Make the bank account and the till

| What | Example |
|---|---|
| A bank account cost centre (type Bank) under the company | BANK0 "Banca – conto corrente", EUR |
| Its IBAN, BIC and holder (Bank statements › Bank accounts › **IBAN**) | IT60X0542811101000000123456, BPMOIT22XXX, Pizzeria Demo Napoli |
| A point of sale with its own settings | POS_PIZ0 "Pizzeria Demo – Via dei Tribunali", a cash desk, bills without a shift |
| The payment methods at the point of sale (Payment methods by cost centre) | Cash → the cash desk; **Bank transfer (QR) → BANK0** |
| The user may work at the point of sale | Users and rights: the point of sale among the user's places |

## 3. The test account at the bank

The sandbox bank **Mock ASPSP** (country **Finland**) holds accounts made from a JSON file uploaded on its accounts page, in the provider's format: one `accounts` entry with `info` (IBAN, currency EUR, name), `transactions` and `balances`. Each transaction has an `entry_reference` (the bank's own id: keep it unique and unchanged, so a later fetch skips what is already held), `transaction_amount`, `credit_debit_indicator` (`CRDT` money in, `DBIT` money out), `status` `BOOK`, `booking_date`, the other party (`debtor` for money in, `creditor` for money out), `reference_number` and `remittance_information`.

The Pizzeria's file (35 lines, 12 October – 14 November 2026, balance €6,074.97: capital, till float, card payouts, payments by QR with the bill's code, suppliers paid by invoice number, wages, a bank fee, the monthly bills) is kept outside the repository, with the samples, as `pizzeria-napoli-eur.json`.

The account's IBAN must be the one typed on the bank account in the ERP: the connection takes the account with that IBAN.

## 4. Connect

1. Bank statements › Bank accounts › **Connect to bank** on BANK0.
2. Country **Finland**, bank **Mock ASPSP**, Business account. The bank starts at the first of the list (e.g. Genome): change it, or the bank's own sign-in page opens, which holds none of the test data.
3. **Go to the bank**, sign in at Mock ASPSP, choose the account, approve.
4. Back at `/bank-connected`: "… is connected to its bank (Mock ASPSP) until …" (180 days).

## 5. Fetch

**Fetch from bank** on BANK0. The first fetch reaches 30 days back; for older lines fetch again with a period. Lines already held are skipped. The bank's balance comes with the statement and is shown next to the ledger's.

Tried: 30 lines from 15 October to 5 November, bank balance €6,067.97; then 12–14 October, 4 lines added, nothing doubled. A fetched line is decided by hand like an uploaded one (match, ignore, clear).

## 6. Pay by bank at the till

The test bank cannot receive a real transfer, so the customer's payment is added to the test account by hand, as a line of today with the bill's amount and code.

1. At the point of sale order something at a table and press pay: note the bill's code (e.g. DC2) and the amount (e.g. 7.00).
2. On Mock ASPSP's page of the account, **Transactions**, add one transaction:

```json
{
  "entry_reference": "PDN202611140034",
  "transaction_amount": { "currency": "EUR", "amount": "7.00" },
  "debtor": { "name": "Cliente Demo QR" },
  "debtor_account": { "iban": "IT55Z0306909606100000099887" },
  "credit_debit_indicator": "CRDT",
  "status": "BOOK",
  "booking_date": "2026-11-14",
  "value_date": "2026-11-14",
  "reference_number": "DC2",
  "remittance_information": ["Pagamento conto DC2"]
}
```

3. At the till choose **Bank transfer (QR)**: the QR code shows the holder, the IBAN, EUR 7.00 and DC2.
4. **Check bank**: the line "Cliente Demo QR · 7.00 · Pagamento conto DC2" is listed. Press **Paid**.

Result: the receipt is posted into BANK0 and the bank line is kept on "Till payments <day>", matched to it; the bill leaves Unpaid.

Do not press Fetch from bank between adding the line and Check bank: once a statement holds the line, Check bank no longer offers it.

Without a connection the till shows **Received** in place of Check bank, and the bank line is matched later on Bank statements.

## Real banks

Production needs a contract with Enable Banking for the company (with their checks on the company), an application in the Production environment, and that application's key in Currencies and payments › Open banking. Enable Banking also offers a free restricted production for the company's own accounts. Reading accounts runs under the provider's licence; starting payments from the ERP would need a payment initiation licence and is not built.

## Related

- [Open banking](open-banking.md)
- [Bank statements](../documents/bank-statements.md)
- [Paying at the till](../point-of-sale/paying.md#pay-by-bank-qr-code)
- [Payment methods](payment-methods.md)

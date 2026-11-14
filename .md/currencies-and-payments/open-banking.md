# Open banking

The company's own open banking provider. The provider reads the company's bank accounts under its own licence, so bank statements can be fetched from the bank and payments by bank checked at the till. Each company has its own provider, application and key, and its own contract with the provider (the founder, 2026-11-14). Gamma holds none.

**Where:** Currencies and payments › Open banking (`/open-banking`, table `tbl_bank_provider`). It is also asked for when a company is made: see [Create a company](../getting-started/create-a-company.md) and [Add a company to a group](../getting-started/add-a-company-to-a-group.md). **Who:** administrators only. The menu item is administrators-only and is not offered on Rights.

## Columns

| Column | Meaning |
|---|---|
| Provider (`kind`) | Which adapter the ERP uses. Today: `EnableBanking` (Enable Banking). |
| Banks (`environment`) | `Sandbox`: the provider's test banks. `Production`: real accounts, which needs a contract with the provider. |
| Application id | The id of the company's application, registered at the provider. |
| Private key | The application's `.pem` file. It is read in the browser, sent once, sealed by the server with its own keys (`DataProtection:KeysFolder`) and **never shown again**. The screen shows only its fingerprint. |
| Return address (`redirect_url`) | Where the bank sends people back after they sign in. It must be one of the application's allowed addresses at the provider. The screen proposes the ERP's own page, `<the ERP's address>/bank-connected`. A local address such as `http://127.0.0.1:8090/bank-connected` works with the Enable Banking sandbox (tried 2026-11-14). |
| Days a bank connection lasts | 1 to 180. How long a bank account's connection asks for; after that it is connected again. |

The API address comes with the provider (`https://api.enablebanking.com`). A company has one provider at a time.

## Actions

### Set the provider

Steps:
1. Open Currencies and payments › Open banking.
2. Choose the provider and the banks (Sandbox or Production), type the application id, choose the `.pem` file, check the return address and the days.
3. Press **Save**.

Result: the provider is saved for the company the user works in. The page shows the provider, the application id, "Set (fingerprint)" for the key, the return address and the days.

Refused when (every reason at once):
- "The ERP has no adapter for …"
- "The environment is Sandbox or Production."
- "The application id is the provider's id of the company's application (no spaces)."
- "Where the bank sends the person back: a full http or https address, one of the application's allowed addresses."
- "A bank connection asks for 1 to 180 days."
- "The private key is not an RSA private key in PEM form (the .pem file the provider gave)." or "The private key is shorter than 2048 bits."
- "Give the application's private key (the .pem file)." when there is none yet.
- Not an administrator: `NotAllowedError: only an administrator sets the company's open banking.`
- At the corporate of a group with no books, or at no company: "Open banking belongs to a company: choose one of its nodes."

API: `PUT /api/v1/open-banking/provider` with `kind`, `applicationId`, `privateKey` (PEM text), `redirectUrl`, `environment`, `accessDays`. The answer never holds the key.

### Change it

Steps: change the fields and press **Save**. Leave the file empty to keep the key already set.

Result: the row changes; the old version is kept as history (with the old key, still sealed).

### See it

API: `GET /api/v1/open-banking/provider`: `kind`, `name`, `environment`, `apiBase`, `applicationId`, `keySet`, `keyFingerprint`, `keyReadable`, `redirectUrl`, `accessDays`. No content when the company has none. `GET /api/v1/setup/bank-providers` lists the providers, environments and the longest connection.

If the server lost its keys (`DataProtection:KeysFolder`), `keyReadable` is false and the page says "The server can no longer open the key: choose the .pem file again."

Claude: `open_banking_status` shows the same (administrators). Claude never sets the key: it must not pass through a chat.

### Switch it off

Steps: press **Switch off…** and confirm.

Result: the company has no provider; nothing is deleted.

API: `DELETE /api/v1/open-banking/provider`.

## Bank accounts and their connections

Each bank account cost centre (a type with "Bank accounts: bank statements are uploaded for them") has one row in `tbl_bank_connection`: its identity, and its connection to the bank when it has one. **Where:** Documents › Bank statements, the **Bank accounts** table. **Who:** the Bank statements screen in your menu.

| Column | Meaning |
|---|---|
| IBAN, BIC, Holder | Typed by hand (enough for a payment QR code without a connection), or given by the bank when the account is connected. The IBAN is checked (ISO 13616, mod 97) and is one bank account's only in the company. The holder has at most 70 characters. |
| Currency | From the bank. |
| Connection | The provider, the bank (`aspsp_name`, `aspsp_country`), personal or business, the bank's session (sealed), the provider's account id, until when (`valid_until`), the last day fetched. |

### Type the IBAN

Steps: press **IBAN** on the account, type the IBAN, BIC and holder, press **Save**.

Refused when: "This is not a valid IBAN."; "Another bank account of the company has this IBAN."; "A BIC has 8 or 11 letters and digits."; the holder is longer than 70; a connected account keeps the IBAN its bank gave ("Disconnect it first to change it").

API: `PUT /api/v1/bank-connections/{bankId}/account` with `iban`, `bic`, `holderName`.

### Connect an account to its bank

Steps:
1. Press **Connect to bank** on the account.
2. Choose the country, the bank (the list comes from the provider), and Business account or Personal account. Press **Go to the bank**. The country starts at the company's and the bank at the first of the list: check both (the test bank, Mock ASPSP, is under Finland).
3. Sign in at the bank and approve.
4. The bank sends you to the return address, `/bank-connected`, which finishes the connection and says "… is connected to its bank (…) until …". Press **Back to bank statements**.

Result: the account shows "Connected at <bank> · until <date>". The account is the one with the IBAN typed before, or the only one the bank gave. When the bank gave several and no IBAN was typed, the account shows them: press **This one** on the right one. Its IBAN, BIC, holder and currency are filled from the bank where they were empty. 14 days before the end it shows "Connect it again soon"; **Connect again** repeats the steps.

Refused when:
- `NotConfiguredError: the company has no open banking provider. An administrator sets it under Currencies and payments › Open banking.`
- "The server can no longer open the provider's key…"
- "Choose the bank."; "The country is its two-letter code."
- The provider refuses: "The provider refused (<status>): <its reason>", or "The provider can't be reached: …".
- On return: "This bank sign-in was not started here, or it is finished already."; "The bank sent no code back: the sign-in was cancelled or refused."; "The bank gave no account with the IBAN …: sign in with the bank and user that hold it." (the bank's session is ended then).
- "Another bank account of the company has this IBAN."

API: `GET /api/v1/bank-connections`, `GET /api/v1/bank-connections/aspsps?country=`, `POST /api/v1/bank-connections/{bankId}/connect` with `aspspName`, `country`, `psuType` (answer: `url`), `POST /api/v1/bank-connections/finish` with `state`, `code`, `POST /api/v1/bank-connections/{bankId}/pick/{accountUid}`.

### Disconnect

Steps: press **Disconnect** and confirm.

Result: the session is ended at the provider and here. The IBAN, BIC and holder stay.

API: `POST /api/v1/bank-connections/{bankId}/disconnect`.

Every read of the bank (fetching a statement, checking a payment at the till) is started by a person and sent with that person's address and browser, so the bank counts it as theirs and not as a background read, which banks cap at about four a day.

## Never copied

The provider is not part of a starter pack. A company copied from another company of the group does not get its provider or key: each company enters its own (the founder, 2026-11-14).

## Related

- [Trying it with the test banks](open-banking-sandbox.md)
- [Paying at the till by bank](../point-of-sale/paying.md#pay-by-bank-qr-code)
- [Bank statements](../documents/bank-statements.md)
- [Payment methods](payment-methods.md)

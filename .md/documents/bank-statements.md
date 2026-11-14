# Bank statements

Upload a bank's file and decide each line by hand: match it to a posted document, write a new document, or ignore it. A statement posts nothing. A line is matched only when a person matches it, or posts a document written from it.

**Where:** Documents › Bank statements. **Who:** the Bank statements screen in your menu. File formats: Currencies and payments › Bank statement formats, administrators write them.

## Bank accounts

Above the statements, the **Bank accounts** table lists the company's bank accounts with their IBAN, holder and connection to the bank: type an IBAN, connect an account to its bank, disconnect it. See [Open banking](../currencies-and-payments/open-banking.md#bank-accounts-and-their-connections).

## File formats

A format says how one bank writes its file (`tbl_bank_statement_format`). The base pack has one: "Date, text, reference, amount": comma separated, one header line, dates as `yyyy-MM-dd`.

| Field | Meaning |
|-------|---------|
| Separator | What separates the columns |
| HeaderLines | Lines at the top that are not statement lines |
| DateColumn, DateFormat | Columns count from 1. The format is exact, for example `dd/MM/yyyy` |
| TextColumn, ReferenceColumn | The text and the reference. The reference is optional |
| AmountColumn | One column, money in positive, money out negative |
| MoneyInColumn, MoneyOutColumn | Or two columns. An empty cell is 0, and money out may be negative |
| DecimalComma | True when the file writes 1.234,56 |

Cells may be quoted. Spaces and apostrophes in numbers are dropped, and so are thousands separators. The file is read as UTF-8.

## Actions

### Upload a statement

Steps:
1. Pick the **Bank**: a cost centre of a type that takes bank statements. In the base pack that is the type Bank. A type takes them when its box "Bank accounts: bank statements are uploaded for them" is ticked: Company › Cost centres › Set up, the type editor. See [Cost centres](../company/cost-centres.md#mark-a-type-as-a-bank-or-a-production-point). With none, the screen says "Add a cost centre of type Bank first."
2. Pick the **File format**. A single choice is picked for you.
3. Pick the file under **Upload statement** (.csv or .txt).

Result: "x lines added, y already held." A new statement holds the new lines. Lines of amount 0 and empty rows are left out. A line counts as already held when that bank has one with the same date, amount, text and reference. Two equal lines in one file are two payments: only as many as are held already are skipped. So a file can be uploaded again safely. If nothing is new, no statement is made.

Refused when:
- The whole file is refused for one bad line: "Line n: the date can't be read." or "Line n: the amount can't be read."
- The file is too large. The limit is 5 MB.
- The bank does not exist. Not one you see.
- The file format does not exist.
- Bank statements are uploaded in a company or one of its cost centres, not at the corporate.
- On screen: "Choose a bank and a file format first."

API: `GET bank-statements/banks`, `GET bank-statements/formats`, `POST bank-statements/import?bankId=&formatId=` with `file` as a form upload. The answer is `{ id, added, skipped }`.

### Fetch from the bank

For an account connected to its bank (see [Open banking](../currencies-and-payments/open-banking.md#connect-an-account-to-its-bank)).

Steps:
1. In the Bank accounts table press **Fetch from bank** on the account.

Result: the account's **booked** lines from the last day fetched (the first time: 30 days back) to today become a new statement, named "<bank> <from> – <to>", like an uploaded one, and are decided the same way. A line whose bank id is already held for that bank is skipped; a line without one is skipped by the upload's rule. Pending lines are left out and come with a later fetch. Each line also keeps who paid or was paid and their IBAN, shown under its text. The screen says "x lines added, y already held, z pending" and "Bank balance on <day>: …. Ledger: …. Difference: …." The bank's balance is kept with the statement and shown in the list's Bank balance column; a difference refuses nothing (the founder, 2026-11-14). With nothing new no statement is made, but the last day fetched moves on.

The read is sent with the person's address and browser, so the bank counts it as theirs. Nothing fetches by itself.

Refused when:
- `NotConfiguredError: the account is not connected to its bank. Connect it on Bank statements.`
- "The bank's access ended: connect the account again."
- "The account was connected through a provider the company no longer uses: connect it again."
- "The server can no longer open the bank session: connect the account again."
- "The period runs from a day to a later one, not after today, at most 730 days."
- The provider refuses: "The provider refused (<status>): <its reason>".

API: `POST /api/v1/bank-connections/{bankId}/fetch?from=&to=` (both optional). The answer: `id` (null when nothing new), `from`, `to`, `added`, `skipped`, `pending`, `bankBalance`, `bankBalanceDate`, `ledgerBalance`. `GET /api/v1/bank-connections/{bankId}/ledger-balance?until=` gives the ledger's balance alone. Claude: `fetch_bank_statement`.

### See statements and lines

Steps:
1. The screen lists each statement: Bank, File, Uploaded, Lines, To decide. Newest first.
2. Press **Lines**.

Result: the lines in date order: Date, Text (with who paid or was paid, for fetched lines), Reference, Amount, and what was decided. A fetched statement's file name starts with "From the bank".

API: `GET bank-statements`, `GET bank-statements/{id}/lines`.

### Match a line

Steps:
1. Press **Match** on an undecided line.
2. Press **Match** beside the document that fits.

Result: the line shows the document's number, which opens it. The document does not change. The offer is the company's posted documents with the bank on the line's side, nearest date first, up to 50:
- Money in: the bank is the document's To side, or the To side of one of its lines. Money out: the From side of the document or of one of its lines.
- The amount equals the document's total in Lcu, or its total in one of its currencies.
- It is not a reversal, not reversed, and not matched to another line.

If a matched document is reversed later, the line shows "Reversed" beside it.

Refused when:
- This statement line is already decided.
- This document can't be matched to this line: it must be posted, of this bank, on the right side, and not matched yet.
- The document's total is not the amount of this line.

API: `GET bank-statements/lines/{id}/choices`, `POST bank-statements/lines/{id}/match/{document}`.

### Write a new document for a line

Steps:
1. Press **Match** on the line. Under the matches is **Write a new document: {type}**, one per type. The types are those that pay open items with a cost centre on the bank's side: Receipt for money in, Payment for money out. A type filed under several categories (Cash desk and Bank) opens in the first by the categories' sort order, then code.
2. Press it. The normal editor opens with the bank and the date filled in, and the statement line shown above the lines.
3. Pick the client or supplier, type the invoice numbers and the amount. See [payments-and-open-invoices.md](payments-and-open-invoices.md).
4. Save and post.

Result: a normal posted document. When it is posted in the editor, the line is matched to it and the editor says "Matched to the bank statement line." If the match is refused, the editor says "Not matched to the bank statement line: match it there.": go back to the statement, press **Match** on the line and match the new document. Saving without posting does not decide the line. The amount is not filled in for you.

API: the editor calls `POST bank-statements/lines/{id}/match/{document}` after posting.

### Ignore a line

Steps:
1. Press **Ignore**.

Result: the line shows "Ignored" and no longer counts as to decide.

Refused when: the line is matched. "This statement line is already decided."

API: `POST bank-statements/lines/{id}/ignore`.

### Undo a line

Steps:
1. Press **Undo** on a matched or ignored line.

Result: the line is undecided again. The document stays as it is.

API: `POST bank-statements/lines/{id}/clear`.

### Remove a statement

For a file uploaded by mistake.

Steps:
1. Press **Remove** on the statement.
2. Confirm: "Remove this statement? It and its lines leave the list. Nothing is deleted: their history stays."

Result: the statement and its lines are switched off. The same file can be uploaded again as new.

Refused when: "This statement has matched lines: undo them first."

API: `DELETE bank-statements/{id}`.

## Related

- [Bank documents](categories/bank.md)
- [Payments and open invoices](payments-and-open-invoices.md)

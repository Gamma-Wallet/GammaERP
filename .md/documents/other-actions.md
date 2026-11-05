# Other actions on a document

History, moving between documents, printing, e-mail, and opening a side's ledger.

**Where:** the editor of a saved document. **Who:** whoever can open the document.

## Actions

### See the history

Steps:
1. Open the **History** tab.

Result: the date and time of each earlier version, newest first. Each save and each post keeps one. "No earlier versions" when the document is as first saved. The screen shows the dates only.

API: `GET documents/{id}/history` gives each version with its content. These are the document's header values; earlier lines are kept as switched-off rows.

### Go to the previous or next document

Steps:
1. Press the left or right arrow at the top.

Result: the document of the same type made just before or after, of those you can see. An arrow is off at the end of the list.

API: `GET documents/{id}/neighbours`.

### Print

Steps:
1. Press **Print**.

Result: a PDF opens in a new tab. It is in the company's language, whatever language the screens are in, and uses the type's print template. An unposted document carries a "not posted" mark, and a reversal says what it reverses.

API: `GET documents/{id}/pdf`. Not found when you can't see the document.

### Send by e-mail

Steps:
1. Press **E-mail**.
2. Type the recipients, separated by commas, semicolons or spaces. Add a subject and a message if wanted.
3. Press **Send**.

Result: the document goes as a PDF attachment.

Refused when (`MailError`):
- This installation has no mail server (Mail:Host and Mail:From).
- "x" is not an e-mail address.
- Send to 1 to 10 addresses.

API: `POST documents/{id}/email` with `{ to, subject, message }`.

### Open a side's ledger

Steps:
1. Beside a picked From or To side, click **Balance {amount}**.

Result: unsaved work is saved first, then the ledger report behind that balance opens, this year so far. The balance shows the side's debit less credit in Lcu. A side with no ledger shows none.

API: `GET cost-centers/{id}/balance` for a cost centre. The screen reads a side's balance by its table.

### Export the list to Excel

There is no button for this on the list.

API: `GET documents/xlsx` with the list's filters, up to 50 000 rows.

## Related

- [The document list](list.md)
- [The life of a document](life-cycle.md)

# The life of a document

What every document goes through, whatever its category: new, draft, posted, reversed. Read this first; the other files give the detail of each step.

**Where:** Documents › any category. **Who:** see "Who may do what" below.

## The states

```
New (on screen only)
  │ Save: gets its number
  ▼
Draft ──── Switch off ───▶ leaves the list, nothing is deleted
  │ Post (on an offer, a contract or an order it books nothing and only makes the document final)
  ▼
Posted: final, read only
  │ Generate › Reverse…
  ▼
Reversal document: posted at once, linked to the original
```

| State | Shown as | Can be changed |
|-------|----------|----------------|
| New | "New" | Yes, nothing is stored until Save |
| Draft | "Draft" | Yes |
| Posted | "Posted" | Never |
| Reversal | "Posted" plus "Reversal of {number}" | Never |

A type marked **Never posted** (offers, contracts, orders, repair documents in the base pack) stays a draft for good. It writes no entries and has no Post button. See [categories](categories/README.md).

## The steps

1. **Start** a document of a type from the list: [list.md](list.md).
2. **Fill** the header and lines, and save as often as needed: [drafts.md](drafts.md), [lines.md](lines.md). The stock check runs on every save.
3. **Look** at the Posting tab: the entries posting would write. [posting.md](posting.md).
4. **Post.** All or nothing. From then on the document is final.
5. **Correct** a posted document only with a reversal, whole or in part: [reversing.md](reversing.md).
6. **Make the next document** from it where a flow is configured, offer to contract to invoice: [flows.md](flows.md).
7. **Close an invoice** by a payment that names its number and covers all that is open: [payments-and-open-invoices.md](payments-and-open-invoices.md).

While a draft is open its user holds it, so nobody else changes it: [drafts.md](drafts.md#holding-a-draft).

## Rules that hold everywhere

- **Nothing runs by itself.** There are no background jobs: a person presses every step.
- **Posted is final.** A posted document and its lines never change and are never unposted.
- **All or nothing.** A save, a post and a reversal each run in one database transaction. A refusal leaves nothing behind.
- **Nothing is deleted.** Switching off hides a row. Each change keeps the old version as a disabled copy.
- **Everything is configuration.** Sides, kinds of line, fields, scenarios, flows and reversal types are database rows. The base pack only proposes them. See [Where each rule is configured](#where-each-rule-is-configured).
- **Lcu** is the company's own currency. Lists and checks work in Lcu.

## Who may do what

An administrator passes every check below. Anyone else needs all that applies:

| Need | Where it is given | What it opens |
|------|-------------------|---------------|
| The category's menu item | Users and rights › Rights, for the user or a role | The item in the menu |
| The document type | System settings › Document types › a type › Users tab (administrators only) | The type in the menu and the New button; saving a document of it. A category item with no type given is hidden |
| Read on the category | The rights on the category (read all, or read own) | The list. "Read own" shows only documents given to the user or a role |
| Write on the category | The rights on the category | Save, post, switch off, hold, reverse |
| The node | The cost centres the user works in | A document is made in a company or one of its cost centres, never at the corporate, and is seen from its node and above |

Actions that make a document need rights on the **new** document: reversing needs the reversal type given and write on its category; a flow needs the target type given and write on its category.

The screens Bank statements, Approvals, Invoice run and Renewals have their own check: the screen must be in the user's menu, on the server too. A contract's billing plan needs the Documents screen, and saving document categories and types needs the Document types screen. Otherwise: `NotAllowedError: this screen is not in your menu.`

## Refusal codes

Most refusals start with a code, then the plain reason.

| Code | Meaning |
|------|---------|
| `PostedError` | The document is posted and final |
| `LockedError` | Another user is editing it |
| `NotAllowedError` | The type or screen was not given to you, or a flow can't be taken now |
| `NotConfiguredError` | The document type's configuration does not have this |
| `FillError` | A line lacks a field its type needs |
| `StockError` | A stock, serial number or lot problem |
| `NoScenarioError` | No posting scenario fits a line |
| `UnbalancedError` | Debit is not equal to credit |
| `GroupAccountError` | An entry would go to an account with accounts under it |
| `MappingError` | An account has no mapped account in another chart of the company |
| `PeriodClosedError` | The posting date is in a closed period |
| `ReversalError` | The reversal is refused |
| `OpenItemError` | A payment names a document that is not open, or does not cover it |
| `CodeLimitError` | The type has used all the numbers of its range |
| `MailError` | The e-mail can't be sent |

## Where each rule is configured

| Rule | Screen | Table |
|------|--------|-------|
| Categories, types, sides, kinds of line, VAT groups and payment methods of a type, reversal type; whether a type holds open items, pays open items, bills by plan, needs approval or sells assets (General tab) | System settings › Document types | `tbl_document_type`, its `_conf_from` / `_conf_to`, `tbl_inter_document_type_*` |
| Which fields a line needs | System settings › Document types › Transaction types | `tbl_transaction_fill` |
| Flows between types | System settings › Document flows | `tbl_document_type_flow` |
| Posting | Accounting setup › Posting scenarios, Scenario lines | `tbl_scenario`, `tbl_scenario_conf` |
| Closed periods | Accounting setup › Closed periods, Close the year | `tbl_posting_lock` |
| Hold timeout, negative stock | Company properties on the cost centre of type Company, found by their Code | `tbl_cost_center_type_info` rows with Code `HOLD_TIMEOUT`, `NEGATIVE_STOCK`; the values in `tbl_cost_center_info` |
| Approval limits | Users and rights › Approval limits | `tbl_approval_limit` |

Configuration rows are written by administrators only.

## Related

- [Categories](categories/README.md)
- [Contracts menu](../contracts/README.md)

# Drafts: writing a document

Filling in a new or unposted document, saving it, switching it off, and how a draft is held while someone works on it.

**Where:** Documents › a category › New, or a draft from the list. **Who:** the document type given to you, and write on the category.

The editor is read only when the document is posted, is a reversal, or is held by another user.

## Actions

### Fill in the header

Steps:
1. **Posting date** and **Document date**: today by default. The posting date is required to post.
2. **Their number**: the other side's number, up to 64 characters kept.
3. **Due date**: only on types that hold open items. Empty means the date plus the partner's payment days, set on posting.
4. **From** and **To**: search and pick. What they offer comes from the type's sides, for example cost centres on one side and clients on the other.
5. **Payment method**: only when the type has any.
6. **Currency**: applies to all the lines. For a foreign currency a **Rate** shows: the day's rate, or type one. The hint says when the day has no rate.
7. Fill in the type's **properties**, if it has any (a contract's start and end date, a carrier).
8. On never-posted types an **Opportunity** can be linked. See the CRM documentation.

Result: the header shows "Not saved" until saved. Beside a picked side its live **Balance** shows; see [other-actions.md](other-actions.md).

### Save

Steps:
1. Press **Save**.

Result:
- A new document gets its number, the type's next code in its range.
- The old lines and totals are switched off, and the lines are written again. Totals are per currency, with the Lcu amounts at the rate.
- Serial numbers and lots on lines are kept with the draft.
- Links to the documents it was created from are updated.
- An approval, or a client's acceptance or refusal, already on the document is cleared: a changed document must be approved or decided again. See [approvals.md](approvals.md).
- A new version shows in History.
- Stock, accounts and balances do not change. Only posting does that.

Refused when:

| Message | Meaning |
|---------|---------|
| Documents are made in a company or one of its cost centres, not at the corporate. | Choose a company node first |
| The document type does not exist. | Switched off, or not this company's |
| `NotAllowedError`: this document type has not been given to you or to one of your roles. | Ask an administrator |
| You may not write elements of this category. | No write right on the category |
| `ReversalError`: a reversal is made only by reversing the document it reverses. | See [reversing.md](reversing.md) |
| `NotConfiguredError`: the document type does not take this payment method. | Set under the type's Payment methods tab |
| `PostedError`: the document is posted and final; correct it with a new document. | Reverse it instead |
| `LockedError`: another user is editing the document. | Wait, see below |
| `CodeLimitError` | The type has used all the numbers of its range |
| A document it was created from does not exist. | The source is not visible to you |
| A line can only come from a line of a document this one was created from. | API only |

Refusals of a line show under that line: see [lines.md](lines.md#refused-on-a-line).

API: `POST documents` with the whole document and its `transactions`. The same call changes a document when `id` is given. A sheet saves through `POST documents/sheet`: see [Journal](categories/journal.md).

### Switch off a draft

Steps:
1. Open the draft and press **Switch off…**
2. Confirm.

Result: the draft leaves the list, with its lines, its totals and any documents under it. Nothing is deleted, its history stays.

Refused when:
- `PostedError`: the document is posted, or a document under it is posted.
- `LockedError`: another user holds it.
- "The document does not exist, or you may not write it."
- "Documents were made from it: switch those off first." An active document was made from this one (by **Generate**, a flow or the invoice run), such as an invoice from a contract or an order from an offer.

API: `DELETE documents/{id}`.

### Holding a draft

A user who opens a draft holds it, so nobody else changes it. The screen does this by itself on opening; there is no button.

Steps (on the screen):
1. Open a draft: you hold it. Any other draft you held is released.
2. While the draft stays open, the editor renews the hold every minute.
3. Leave it: the hold is released.
4. Sign out: every draft you hold is released.

Result: another user opening it sees "Another user is editing this document. It is open read only until they close it." Reading stays open to them. A reversal or a posted document is never held.

A hold ends when its user leaves, when the user signs out, when the document is posted or switched off, or after the company's timeout. The timeout is the company property with the Code `HOLD_TIMEOUT` (named `Hold timeout (minutes)` in the pack; the name is not read). Empty means a hold lasts until it is released. Because the editor renews the hold, the timeout counts from when the editor was last open. The renewal is not a background job: it runs only while the user has the document open.

Refused when:
- `LockedError`: someone else holds it (hold, save, post, switch off).
- `PostedError`: a posted document is not held.
- "Only a user holds a document."

API: `POST documents/{id}/hold` (also the renewal), `POST documents/{id}/release`, `GET documents/{id}/holder` gives `{ holder }`.

### Let go of all your drafts

The screen does this when you sign out.

Result: every draft you hold is released, so others can edit them.

API: `POST documents/release-all`.

## Open points

- With no timeout, a hold left by a browser closed without signing out stays until that user opens and leaves the document, or signs out.

## Related

- [Lines](lines.md)
- [Posting](posting.md)
- [The life of a document](life-cycle.md)

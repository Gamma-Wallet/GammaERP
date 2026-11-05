# The document list

One list per document category: its documents, filtered and sorted, and the way to a new document.

**Where:** Documents › a category, for example Documents › Sales. A menu item may also open one type only. **Who:** read on the category. The New button shows only the types given to the user.

## Actions

### See the documents of a category

Steps:
1. Open Documents › the category.

Result: 50 documents a page, the newest first. Columns: Number, Posting date, From, To, Their number, Currency, Rate, Net, VAT, Total, State. Net, VAT and Total are in Lcu. Currency and Rate are those of the document's first total. State shows Draft, Posted or "Reversal of {number}".

Refused when: without read on the category the list is empty.

API: `GET documents?category={id}&page=1&pageSize=50`. The page size is 1 to 500.

### Filter

Steps:
1. Click a type chip to see one type. It shows when the category has more than one type.
2. Click **Drafts** or **Posted** to see one state.
3. Type or pick in Search (number or name), From, To, Posted from, Posted to.
4. Click **More filters** for Number, Their number, Dated from, Dated to, Total from, Total to, Written by. The closed button says how many of these still apply.

Result: the list reloads from page 1. Text filters match anywhere in the text, case aside. Totals compare with the document's total in Lcu. Written by is the user who last changed the document.

API: the same call with `type`, `search`, `code`, `from`, `to`, `externalNumber`, `posted`, `postedFrom`, `postedTo`, `datedFrom`, `datedTo`, `createdBy`, `createdFrom`, `createdTo`, `totalFrom`, `totalTo`.

### Sort

Steps:
1. Click a sortable column head: Number, Posting date, From, To, Their number, Total.

Result: the server sorts all pages. Sorting by Number follows the order the documents were made in.

Refused when: an API call sorts by another column. "A list is sorted by one of: …".

API: add `sort=code|postingDate|documentDate|from|to|externalNumber|total|createdOn` and `descending=true`.

### Open a document

Steps:
1. Click its row.

Result: the editor opens. A draft is held for you, unless someone else holds it. See [drafts.md](drafts.md#holding-a-draft).

API: `GET documents/{id}`. Not found when you can't see it.

### Start a new document

Steps:
1. Press **New {type}** when the category has one type, or **New** and pick the type.

Result: an empty document dated today, in this category. Nothing is stored until Save. Types under another type (the journals of a sheet) are not offered here: they are rows of their sheet.

Refused when: no type of the category was given to you, so the item is not in your menu. In the Reversals category the button shows, but saving is refused: see [reversing.md](reversing.md).

API: `GET documents/types/allowed?category={id}`, then `GET documents/types/{id}/form`.

## Note: a type in two categories

Payment, Receipt, Prepayment, Advance and Employee advance are in both Cash desk and Bank in the base pack. A document is filed under the category it was started from and lists only there. A document made by a flow or a reversal takes all the categories of its type. A repeat keeps those of the document it repeats.

**Excel** at the top of the list downloads the documents as the list shows them, with its filters and order (`GET /api/v1/documents/xlsx`, the same parameters as the list without the page).

## Related

- [The life of a document](life-cycle.md)
- [Drafts](drafts.md)

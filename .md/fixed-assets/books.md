# Depreciation books

A book keeps one set of asset values and depreciation, such as a company book and a tax book. The starter pack has both: Company book posts, Tax book does not.

**Where:** Fixed assets › Depreciation books (`/config/depreciation-books`, table `tbl_depreciation_book`). **Who:** an administrator changes; see [configuration-lists.md](../accounting/configuration-lists.md).

## Columns

| Column | Meaning |
|--------|---------|
| Name, Description | What people read. |
| Method | The method its assets use. |
| Chart category | The chart it belongs to. |
| Posts | Ticked: its journals are posted when made. It is also the book a sale of an asset reads. |

## Actions

### Add or change a book

Steps:
1. Press **Add**, or click a book.
2. Fill in the columns and press **Save**.

Result: the book is offered on Depreciation and Remaining asset value. Each book needs its own depreciation scenarios before it can make journals.

Refused when: see [configuration-lists.md](../accounting/configuration-lists.md).

API: `POST /config/depreciation-books`.

### Switch a book off

Steps: open it, press **Switch off…** and confirm.

Result: it is no longer offered. Nothing is deleted.

API: `DELETE /config/depreciation-books/{id}`.

## Related

- [methods.md](methods.md)
- [depreciation-scenarios.md](depreciation-scenarios.md)
- [selling-an-asset.md](selling-an-asset.md)

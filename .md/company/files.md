# Files

The files of the cost centre you work at: upload, search, download and remove.

**Where:** Company › Files, or the Files button in the top bar. **Who:** the `files` screen in your menu, on the server too.

## Actions

### Find a file

Steps:

1. Open the screen.
2. Type in **Search by name**.

Result: the files of your cost centre and those below it, newest first, 50 to a page, with Name, Type, Size and Uploaded.

Refused when: `NotAllowedError: this screen is not in your menu.` (403)

API: `GET /files?search=&page=&pageSize=`, `GET /files/{id}/info`

### Upload a file

Steps:

1. Press **Upload**.
2. Choose a file.

Result: the file is stored for the cost centre you work at. The name, type, size and SHA-256 go to `tbl_stored_file`, and the bytes to the store under a key Gamma makes, never the file's own name. The file is read whole first, so nothing is kept unless all of it arrived and fits.

Accepted types: PDF, PNG, JPEG, WebP, GIF, plain text, CSV, XML, Excel (.xlsx), Word (.docx). The server can change the list with `Files:ContentTypes`.

Refused when:

- `FileError: the file needs a name of at most 255 characters.`
- `FileError: files of type <type> are not accepted.`
- `FileError: the file is larger than <n> bytes.` The limit is `Files:MaxBytes`, 10 MB by default.
- `FileError: the file is empty.`

API: `POST /files`, multipart, field `file`.

### Download a file

Steps:

1. Press **Download** on the row.

Result: the browser saves the file under its own name. It is sent so that the browser never runs it or shows it in the page. Its bytes are checked against the saved hash.

Refused when:

- The file is not one you see: 404, "This does not exist, or it is not open to you."
- The store has lost it, or its bytes no longer match. The API fails with a server error.

API: `GET /files/{id}`

### Remove a file

Steps:

1. Press **Remove** on the row.
2. Confirm. The dialog says: "The file is switched off; a property that still uses it keeps it."

Result: the file is switched off and leaves the list. It is never deleted: its row in `tbl_stored_file` and its bytes stay.

Refused when:

- "A property still uses this file: take it off there first.": an active property value points at it.
- "The file does not exist.": it is not one you see, or it is already removed.

API: `DELETE /files/{id}`

## Related

- [Menu access](../users-and-rights/menu-access.md)
- [Architecture: file storage](../architecture.md#file-storage)

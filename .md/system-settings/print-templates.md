# Print templates

Per document type, the printed title and a closing text.

**Where:** System settings › Print templates. **Who:** anyone with it in their menu reads. An administrator changes rows. The screen's generic actions are in [Configuration screens](configuration-screens.md).

Configuration: `tbl_template`, model `templates`. The starter pack has no rows.

| Field | Column | Used by the print |
|---|---|---|
| Document type | `DocumentTypeId` | Yes: the type it applies to. |
| Long name | `LongName` | Yes: the title, in place of the type's name. |
| Body | `Body` | Yes: a paragraph under the lines. |
| Short name | `ShortName` | No |
| Pos | `Pos` | No |
| Printer name | `PrinterName` | No |
| Copies | `Copies` | No |

## Actions

### Add or change a template

Steps:

1. Press **Add**, or press a row.
2. Choose the Document type. Fill in the Long name and the Body.
3. Press **Save**.

Result: the next PDF of a document of this type uses them. A document goes out in the company's language. A document not posted says so on the print.

With no template, the title is the document type's name.

A document type has one active template at most.

Refused when:
- "This document type has an active print template already: switch that one off first."
- As in [Configuration screens](configuration-screens.md).

API: `GET /config/templates`, `POST /config/templates`, `DELETE /config/templates/{id}`. The print itself: `GET /documents/{id}/pdf`.

## Related

- [Document types](document-types.md)
- [Configuration screens](configuration-screens.md)

# Skins

The look of the screens: colours, fonts, corner roundness and density. A skin never changes the layout. One skin is in use for the ERP screens and one for the point of sale.

**Where:** System settings › Skins. **Who:** administrators only. The item is not offered on Rights. The skin in use is read by everyone signed in to the company.

Configuration: `tbl_skin` (one row per skin: `Name`, `Kind` erp or pos, `Dark`, `InUse`, `Tokens`). The starter packs give every company four rows: **Harbour** for the ERP and for the point of sale, in use, and **Nightshift**, the dark skin, for both. With no skin in use the screens show the built-in look, which is Harbour.

## What a skin is

A skin is a JSON file:

```json
{ "name": "Harbour", "kind": "erp", "dark": false, "tokens": { "canvas": "#f1f3f6", "surface": "#ffffff", "topbar": "#11233a" } }
```

`tokens` holds values for the screens' design tokens, by name without the leading `--`. The names are those of `src/web/src/styles/_tokens.scss`. A token the file leaves out keeps the built-in value. The point of sale's skin (`"kind": "pos"`) usually has bigger text and touch targets.

## Actions

### Upload a skin

Steps:
1. Press **Upload a skin** and choose the file.

Result: the skin is added to its list, not in use. If a text colour is hard to read on its background, the screen says which pair and its contrast; the skin is added all the same. Checked pairs: ink, ink-2, muted and link on surface (4.5:1), primary-ink on primary (4.5:1), topbar-ink on topbar (4.5:1), line-strong on surface (3:1).

Refused when:
- "The file is not JSON." or "A skin file has a name, a kind (erp or pos), dark (true or false) and its tokens."
- "A skin has a name of up to 60 characters."
- "A skin is for the ERP (erp) or the point of sale (pos)."
- "A skin sets between 1 and 300 tokens."
- "A token name is lowercase letters, digits and hyphens, such as surface or text-m."
- "A value is a plain CSS value of up to 200 characters: a colour, a size, a number, font names or a shadow." No links (`url(`), no code, none of `; { } < > \ @` or double quotes.
- "The app does not ship {fonts}: use …": a font token names a font the app doesn't include. The app ships Inter, Source Serif 4 and Newsreader, besides the generic families.
- Not an administrator: `NotAllowedError: only an administrator chooses the skins.`

API: `POST /skins` with `{ name, kind, dark, tokens }`. The answer holds the skin and `warnings`.

### Preview a skin

Steps:
1. Press **Preview** on an ERP skin.

Result: the screens take the skin at once, in this browser only. Nothing is saved. Press **End the preview**, or leave the screen, to go back to the skin in use. A point-of-sale skin has no preview here: put it in use and open the point of sale.

### Put a skin in use

Steps:
1. Press **Use** on the skin.

Result: it is the company's skin for its kind; the one in use before is no longer. Everyone sees it the next time their screens load the menu (on signing in or choosing where they work). An ERP skin applies here at once.

API: `POST /skins/{id}/use`

### Go back to the built-in look

Steps:
1. Press **Use the built-in look** above the list.

Result: no skin is in use for that kind; the screens show the built-in look (Harbour).

API: `POST /skins/built-in?kind=erp|pos`

### Download a skin

Steps:
1. Press **Download** on the skin.

Result: the skin's file (`<name>-<kind>.json`), to change and upload as a new skin.

API: `GET /skins/{id}/file`

### Switch a skin off

Steps:
1. Press **Switch off** on a skin that is not in use.

Result: it leaves the list. Nothing is deleted.

Refused when: "This skin is in use: choose another one first."

API: `DELETE /skins/{id}`

The skin in use for a kind: `GET /skins/in-use?kind=erp|pos`, for everyone signed in.

## Related

- [Print templates](print-templates.md)
- [Configuration screens](configuration-screens.md)

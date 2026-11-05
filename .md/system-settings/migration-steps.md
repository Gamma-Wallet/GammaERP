# Migration steps

The list behind the Migration screen. The order lives here. The engine knows only how to read and write each kind of list.

**Where:** System settings › Migration steps. **Who:** anyone with it in their menu reads. An administrator changes rows. The screen's generic actions are in [Configuration screens](configuration-screens.md).

Configuration: `tbl_migration_item`, model `migration-items`.

| Field | Column | Meaning |
|---|---|---|
| Code | `Code` | The step's code. |
| Name | `Name` | Its name. |
| Kind | `Kind` | How the list is read and written. |
| Entity | `Entity` | The entity of an Elements or Categories list. On an Opening list, its line kinds, separated by commas. |
| Requires | `Requires` | The codes of the steps it needs, separated by commas. |
| Offset account | `OffsetAccount` | On an Opening list, the account code its sheet balances on, which stands at zero once every step is in. |
| Document type | `DocumentTypeId` | On an Opening list, the journal type of its rows. |
| Sort order | `SortOrder` | Its order among steps at the same place. |

Kinds: Elements, Categories, DocumentTypes, Types, LineKinds, PostingGroups, Prices, Rates, Numbers, Opening, OpeningStock, OpeningAssets, VatGroups, DocumentVat, VatBoxes, VatRules.

## The steps in the pack

| Code | Name | Kind | Requires |
|---|---|---|---|
| `CHART` | Chart of accounts | Elements | |
| `VAT_GROUPS` | VAT groups | VatGroups | |
| `DOCUMENT_CATEGORIES` | Document categories | Categories | |
| `DOCUMENT_TYPES` | Document types | DocumentTypes | `DOCUMENT_CATEGORIES` |
| `LINE_CATEGORIES` | Transaction categories | Categories | `DOCUMENT_TYPES` |
| `LINE_TYPES` | Transaction types | Types | `LINE_CATEGORIES` |
| `LINE_KINDS` | Transaction types of document types | LineKinds | `DOCUMENT_TYPES`, `LINE_TYPES` |
| `DOCUMENT_VAT_GROUPS` | VAT groups of document types | DocumentVat | `DOCUMENT_TYPES`, `VAT_GROUPS` |
| `VAT_RETURN_BOXES` | VAT return boxes | VatBoxes | |
| `VAT_RETURN_BOX_RULES` | VAT return box rules | VatRules | `VAT_RETURN_BOXES`, `VAT_GROUPS`, `DOCUMENT_TYPES`, `LINE_TYPES` |
| `POSTING_GROUPS` | Posting groups | PostingGroups | `CHART`, `LINE_KINDS` |
| `COST_CENTERS` | Warehouses, banks, cash desks and places | Elements | `CHART` |
| `PARTNER_CATEGORIES` | Client and supplier categories | Categories | |
| `PRODUCT_CATEGORIES` | Product categories | Categories | |
| `UNITS` | Units | Elements | |
| `PARTNERS` | Clients and suppliers | Elements | `PARTNER_CATEGORIES` |
| `PRODUCTS` | Products and services | Elements | `PRODUCT_CATEGORIES`, `UNITS`, `VAT_GROUPS` |
| `PRICES` | Sale prices | Prices | `PARTNER_CATEGORIES`, `PRODUCTS` |
| `STAFF` | Staff | Elements | |
| `RATES` | Exchange rates | Rates | |
| `NUMBERS` | Next document numbers | Numbers | `DOCUMENT_TYPES` |
| `OPENING_PARTNERS` | Opening balances: clients and suppliers | Opening | `PARTNERS`, `POSTING_GROUPS`, `VAT_GROUPS` |
| `OPENING_CASH` | Opening balances: banks, cash desks and staff | Opening | `COST_CENTERS`, `POSTING_GROUPS`, `VAT_GROUPS` |
| `OPENING_STOCK` | Opening balances: stock | OpeningStock | `COST_CENTERS`, `PRODUCTS`, `POSTING_GROUPS`, `VAT_GROUPS` |
| `OPENING_ASSETS` | Opening balances: fixed assets | OpeningAssets | `PRODUCTS`, `POSTING_GROUPS`, `VAT_GROUPS` |
| `OPENING_CHART` | Opening balances: the chart | Opening | `OPENING_PARTNERS`, `OPENING_CASH`, `OPENING_STOCK`, `OPENING_ASSETS` |

The five opening steps balance on account `890` in the base pack. When the chart's Migration step gives that account a New code, their Offset account follows.

## How the tree is built

- A step needs what its Requires names, and everything those need in turn.
- It stays closed while any step above it holds nothing.
- It shows under the last of what it needs to come ready.

## Actions

### Add or change a step

Steps:

1. Press **Add**, or press a row.
2. Fill in the form. A new step needs a Kind the engine reads.
3. Press **Save**.

Result: the Migration screen shows it at its place.

Refused when, on saving:

- `A migration step reads <kinds>.`
- as in [Configuration screens](configuration-screens.md).

Refused when, on opening Migration:

- `NotConfiguredError: the step <code> needs <name>, which is no step.`
- `NotConfiguredError: the steps need each other: <path>.`
- `NotConfiguredError: the step <code> has the kind <kind>, which can't be read.`
- `NotConfiguredError: the step <code> names the entity <entity>.`

API: `GET /config/migration-items`, `POST /config/migration-items`, `DELETE /config/migration-items/{id}`

## Related

- [Migration](migration.md)
- [Configuration screens](configuration-screens.md)

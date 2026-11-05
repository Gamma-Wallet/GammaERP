# Architecture

How Gamma ERP is built: its projects, its data pattern, and how a request and a posting pass through it. Facts come from the code in `src/` and `deploy/`.

```text
 Browser (Angular 21, src/web)
    |  HTTPS. Header X-Gamma-Node = the cost centre the user works at
    v
 Caddy (web container)  ---- static screens from /srv
    |  /api /account /connect /.well-known /health
    v
 Gamma.Erp.Api
    ErrorMiddleware > Authentication (OpenIddict) > RateLimiter > RequestContextMiddleware
    Endpoints.cs, LookupEndpoints.cs, Auth/, Setup/
    |
    v
 Gamma.Erp.Services   rules: Entities, Documents, Posting, Configuration, Migration, Pos, Stock, Reports...
    |
    v
 Gamma.Erp.Persistence   ErpDbContext, ModelMapping, HistoryInterceptor, TenantConnectionInterceptor
    |                    (uses Gamma.Erp.Models)
    v
 PostgreSQL 16   15 business schemas with row-level security, plus the auth schema

 Gamma.Erp.Seed (starter packs)      used by Setup/ and create-company
 Gamma.Erp.Migrations (Initial)      run as the /app/migrate bundle when the container starts
```

## Projects and layers

| Project | Role | Uses |
|---|---|---|
| `src/backend/Gamma.Erp.Models` | Classes of every table and their DTOs. One folder per area. | nothing |
| `src/backend/Gamma.Erp.Persistence` | `ErpDbContext`, `ModelMapping` (maps every model the same way), `HistoryInterceptor`, tenancy. | Models |
| `src/backend/Gamma.Erp.Migrations` | The EF migration `Initial` and the design-time factory. | Persistence |
| `src/backend/Gamma.Erp.Seed` | Starter packs (`Packs/<country>/`, embedded), `Organisation`, `CompanyCopy`, `ActivityCatalogue`. | Persistence |
| `src/backend/Gamma.Erp.Services` | All business rules. Refusals are `ValidationException` with a code from `ErrorCodes`. | Persistence |
| `src/backend/Gamma.Erp.Api` | REST under `/api/v1`, sign-in, set-up, the `create-company` command, translated messages. | Services, Seed, Migrations |
| `src/web` | The screens (Angular 21 with its own kit in `src/app/ui`). | the API |
| `tools/starter-packs` | Pack sources (`base/*.json`) and Python scripts. | |
| `tools/Gamma.Erp.DevSeed` | Fills an empty local database with an example group. | |
| `tests/` | Persistence, Services and Api test projects. | |

## The entity pattern

Partners, products, cost centres, documents, transactions, charts, units, employees, users and opportunities all have the same tables. The base classes are in `Gamma.Erp.Models/Base`.

| Table (example for products) | Base class | Holds |
|---|---|---|
| `tbl_product_category` | `Category` | A tree of categories. A category is a folder or holds rows (`IsElement`). |
| `tbl_product_type` | `ElementType` | A tree of types, with the code range (`CodeStart`, `CodeEnd`, `CodeCurrent`). |
| `tbl_inter_product_type_category` | `InterTypeCategory` | Which categories a type belongs to. |
| `tbl_product_type_info` | `TypeInfo` | A dynamic property of a type, with its kind (`PropertyKind`). |
| `tbl_product_type_info_choice` | `TypeInfoChoice` | The choices of a list property. |
| `tbl_product` | `Element` | The row itself: code, name, description, parent, type. |
| `tbl_product_info` | `ElementInfo` | One property value of one row, as JSON. |
| `tbl_inter_product_category` | `InterElementCategory` | Which categories a row is in. |
| `tbl_product_access` | `ElementAccess` | Which users or roles a row is given to. |

One generic class, `EntityService<...>`, applies the rules to all of them. `Endpoints.MapEntity` gives each one the same routes. The rows that say "this table, this category, this type" are in `tbl_elements`: lookups, scenarios and rights all point at them.

Rules on any row of an entity:

- A row without a parent needs a top-level type. A row under a parent needs a type under the parent's type, or the same type.
- Two active rows at one level with the same name are refused (`NameError`).
- A row with active rows under it is not switched off (`ChildrenExistError`).
- Codes stay inside the type's range (`CodeLimitError`).

## Schemas

A model's schema is its folder in snake_case: `charts`, `cost_centers`, `currencies`, `depreciation`, `documents`, `employees`, `opportunities`, `partners`, `payments`, `platform`, `posting`, `products`, `transactions`, `units`, `users`. OpenIddict's tables are in `auth`.

## Row-level security and tenants

A tenant is one corporate: a group or a single company with everything under it.

1. The token carries the claim `tenant`. `RequestContextMiddleware` sets it on `TenantContext` before anything is read.
2. `TenantConnectionInterceptor` runs on every connection: `set_config('app.tenant_id', ...)`, then `SET ROLE gamma_erp_tenant`.
3. `Initial` turns on row-level security on 186 tables, with the policy `tenant_isolation` on `tenant_id`. The tenant role gets SELECT, INSERT and UPDATE, never DELETE.
4. Without a tenant (start-up, set-up, the sign-in lookup) the connection keeps the owner role, which the policy does not restrict.

Inside a tenant, the **node** decides what is seen. The node is the cost centre in the `X-Gamma-Node` header. `Visibility` applies it:

- A request sees rows owned by its node and the nodes below it, stopping at a company below it. It never sees what is above it. A group does not see its companies' rows.
- Configuration (`ConfigurationNodesAsync`) also reaches up to the company, because a company's configuration applies to all its cost centres.
- `Row.OwnerId` empty means the organisation's own definitions, which every node reads.
- A company is a node whose type has `KeepsBooks`, or a root. A company is the root or directly under it, never under another company (`CompanyLevelError`).

## Soft delete and row history

`HistoryInterceptor` runs on every save, for every table:

- A delete becomes `Active = false`. Nothing is removed.
- A change first copies the stored row into a new row with `Active = false`. The live row keeps its id and points at the copy through `IdChange`. Each copy points at the one before it.
- `CreatedOn` and `CreatedBy` say when and by whom this version was written.
- A disabled row never changes, except by being switched back on.
- A global filter makes reads see active rows only.

Written by plain SQL without a history copy: password hashes, sign-in counters, and a type's next code.

User activity needs no log of its own: it reads these versions. See [User activity](users-and-rights/user-activity.md).

## The posting engine

Posting is `DocumentService.PostAsync`. It is one transaction: all or nothing. Policy is not in C#. The engine reads database rows and guarantees only integrity.

| What | Configuration read |
|---|---|
| What each side may be, and what goes into its balance, product value and cost layers | `tbl_document_type_conf_from`, `tbl_document_type_conf_to` |
| Which transaction types a document type holds | `tbl_inter_document_type_transaction_type` |
| How a line is filled | `tbl_transaction_fill` |
| What a line picks from, and whether it moves stock | `tbl_transaction_type_conf` |
| Which accounts, sides and amounts | `tbl_scenario`, `tbl_scenario_conf` |
| What an amount reads | "Transaction amount" rows of `tbl_elements` |
| Further charts | `tbl_inter_chart_chart` |
| Closed periods | `PostingLock` |

Order of work in `PostOneAsync`:

1. Checks: never-posted type, posting date, closed period, lines filled, exchange rates, shift.
2. `MovementService` plans stock and balances. It gives each line its stock cost, first in, first out.
3. `PostingService` finds the most specific scenario for each line and works out every entry.
4. It refuses when no scenario fits (`NoScenarioError`), a line names no amount (`NotConfiguredError`), debit differs from credit (`UnbalancedError`), or an entry goes to a group account (`GroupAccountError`).

A posted document is final. It is corrected by a reversal, a document of the reversal type named on its type.

## Starter packs and how a company is created

A pack is a set of JSON files, one per model, in `Gamma.Erp.Seed/Packs/<country>/`, for 30 countries. In a pack each row has a key and refers to other rows by key, so the same pack can load onto many companies with new ids. Every row loaded is owned by the company.

- **Groups** (`Packs/_catalogue/groups.json`): `chart`, `documents`, `posting` (needs chart and documents), `depreciation` (needs chart). Everything else always loads.
- **Profiles** (`Packs/<country>/profiles/`): `ACCOMMODATION`, `BEAUTY`, `HOSPITALITY`, `RETAIL`, `SERVICE`, `WHOLESALE`. A profile keeps only the codes it lists.
- A row pointing at a row not loaded is left out too, and so on down.
- **Activities** (`Packs/_catalogue/activities.json`): a tree from `GENERAL`. A company takes the profile of its nearest level that has one.

There is no public sign-up. The first company is made on the server by the `create-company` command. See [Create a company](getting-started/create-a-company.md). Further companies of a group start from the entry page. See [Add a company to a group](getting-started/add-a-company-to-a-group.md).

## The EF migration

The business schema has one migration, `20261102201304_Initial`, in `Gamma.Erp.Migrations`. It makes the schemas and tables, then sets up the tenant role and row-level security through the `RowLevelSecurity` helpers.

The sign-in tables have their own migration, `20261029182131_Auth`, in `Gamma.Erp.Api/Auth/Migrations`. The API applies it itself at start-up.

## Authentication

- **Server:** OpenIddict in the API. Authorization code flow with PKCE, plus refresh tokens. The client is `gamma-erp-web`, public, with redirect addresses from `Auth:RedirectUris`.
- **Endpoints:** `/connect/authorize`, `/connect/token`, `/connect/logout`.
- **Lifetimes:** the access token lasts 30 minutes. The sign-in cookie `gamma-erp-login` lasts 8 hours.
- **Sign-in pages:** plain HTML from the API: `/account/login`, `/account/forgot`, `/account/reset`.
- **Passwords:** PBKDF2 hashes, at least 12 characters. 5 wrong passwords lock a user for 15 minutes. One network address gets `Auth:SignInLimitPerMinute` tries a minute, 10 by default.
- **Keys:** PFX files from the host. Without them the keys last only while the process runs.

Access has four layers:

| Layer | Where | Enforced by |
|---|---|---|
| Menu screen | Rights › Menu | `RequireScreen`, `RequireReport`, `Visibility.OpensScreenAsync` |
| Entity and category | Rights › Registers | `Visibility.RightsAsync` |
| Document type | Document types › Users | `DocumentService` |
| Administrator | A role with `IsAdministrator` | `Administrator()` checks in services |

The menu is the rule on the server too: a route that needs a screen answers 403 when that screen is not in the user's menu. An administrator passes every screen check, but still sees only at their node and below.

## Languages

Each company has its own language plus English (`CostCenter.Language`, taken from the country's pack).

- **Stored names:** `TranslatedText`, jsonb such as `{"en": "...", "sq": "..."}`. On save, a text missing in one of the two languages is copied from the other.
- **Web:** `core/i18n.ts`. The English text is the key. The words are in `public/i18n/<language>.json` (`sq`, `it`, `el` today) and load before the app starts. A word without a translation stays in English.
- **Server:** `Messages.Translate` uses `Gamma.Erp.Api/i18n/<language>.json` (`sq` today), English sentences as keys, `{0}` for values. A leading code such as `StockError:` stays as it is. Every call carries `?language=` when the screens are not in English.
- **Chart names** stay in the country's language.
- **Printed documents** use the company's language, whatever the screens are in.

## File storage

`IFileStore` is chosen at start-up by `Files:Store`. `Disk` is the only store today. `tbl_stored_file` holds the name, content type, size, SHA-256 and storage key `<tenant>/<file id>`, and the bytes are in `Files:Root`. The limit is `Files:MaxBytes`, 10 MB by default, and accepted content types are in `FileOptions`. Every read is checked against the hash. See [Files](company/files.md).

## Deployment

The `deploy/` folder holds everything, and `deploy/README.md` is the guide.

| File | Role |
|---|---|
| `Dockerfile` | Image `gamma-erp`: the API plus the migrations bundle `/app/migrate`, port 8080. |
| `web.Dockerfile` | Image `gamma-erp-web`: the Angular build served by Caddy. |
| `Caddyfile` | Passes `/api`, `/account`, `/connect`, `/.well-known`, `/health` to `erp`. Everything else is the screens. |
| `entrypoint.sh` | Runs `/app/migrate` unless `MIGRATE_ON_START=false`, then starts the API. |
| `docker-compose.yml` | `db` (PostgreSQL 16), `erp`, `web`. Only `web` and `db` publish a port, both on `127.0.0.1`. |
| `.env.example` | The settings to copy to `.env`. `FORWARDED_HEADERS=true`: `erp` publishes no port, so `web` is the API's only way in and its `X-Forwarded-*` headers are trusted. |
| `certs/` | The token keys, mounted read-only at `/certs`. |

Volumes: `db-data` holds the database. `erp-data` (`/data`) holds uploaded files and sign-in cookie keys. Back up both.

Nothing runs by itself. There are no background jobs, except that a forgotten password's e-mail is sent outside its request.

# Gamma ERP

An open-source ERP for small and medium businesses. It covers:
- **Documents:** invoices, offers, contracts, orders, payments, reversals.
- **Accounting:** a configurable chart and posting scenarios, closed periods, the year-end close.
- **Stock and production:** lots, serial numbers, recipes, disassembly.
- **Point of sale:** shops with tills, restaurants with tables, events, hotels, repairs.
- **Fixed assets** and **CRM**.

Each company works in its own language plus English, starting from its country's configuration pack.

Everything the ERP does comes from configuration in the database: document types, posting rules, accounts, numbering, VAT and roles. The code only keeps the books whole: all or nothing, debit equals credit, nothing deleted, history kept.

Gamma ERP also has an **MCP server**, so Claude can work in it for a user, with that user's rights.

## Run it

The quickest way is Docker. The images are published for amd64 and arm64 on the GitHub Container Registry
(`ghcr.io/gamma-wallet/gamma-erp-api` and `ghcr.io/gamma-wallet/gamma-erp-web`):

```sh
git clone https://github.com/Gamma-Wallet/GammaERP.git
cd GammaERP/deploy
cp .env.example .env      # set real values; .env is never committed
docker compose up -d      # or: docker compose up -d --build, to build the images from this checkout
```

Gamma ERP is in its **Gamma** release stage: usable, still growing, some modules not finished yet.

There is no public sign-up. The first company and its administrator are made on the server with `create-company`, which prints the first password and Claude token once. See [deploy/README.md](deploy/README.md).

## What's inside

| Path | What |
|---|---|
| `src/backend` | The API: .NET 10, ASP.NET Core, EF Core on PostgreSQL, OpenIddict for sign-in. The MCP server is in `Gamma.Erp.Api/Mcp`. |
| `src/web` | The screens: Angular 21 with its own components on the Angular CDK. |
| `src/mcp-plugin` | A Claude Code plugin: the MCP connection and a skill. |
| `tools/starter-packs` | The scripts that build each country's configuration pack. |
| `tests` | Unit, integration and end-to-end tests. |
| `.md` | The user guide: every screen and action, with its rights, results, refusals and API. |
| `deploy` | Docker images, compose file and settings. |

## Develop

- **Backend:** `dotnet build src/backend/Gamma.Erp.Api/Gamma.Erp.Api.csproj`. The database schema is one EF Core migration in `src/backend/Gamma.Erp.Migrations`.
- **Web:** `cd src/web && npm install && npx ng serve`. The development server sends API calls to `http://localhost:5080`.

See [CONTRIBUTING.md](CONTRIBUTING.md) before sending a change.

## Licence

Copyright © 2026 Gamma Wallet Ltd (England and Wales, no. 17168282).

Gamma ERP is free software under the [GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0-only):
- You may use, study, change and self-host it.
- If you offer a changed Gamma ERP to others over a network, you must make your changed source available to them under the same licence.

The bundled fonts (Inter, Newsreader, Source Serif 4) are under the SIL Open Font License. The texts for the copies served to the sign-in page are in `src/web/public/fonts`; the rest come with their npm packages. Dependencies keep their own open-source licences.

## Security

Report a vulnerability privately: see [SECURITY.md](SECURITY.md).

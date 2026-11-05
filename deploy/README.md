# Running Gamma ERP with Docker

Two images, published for amd64 and arm64 on the GitHub Container Registry with every release:

- `ghcr.io/gamma-wallet/gamma-erp-api` (`Dockerfile`) holds the API and the database migrations. When the container starts, it brings the database up to the
  image's schema, then starts the API on port 8080.
- `ghcr.io/gamma-wallet/gamma-erp-web` (`web.Dockerfile`) holds the screens (the Angular build) served by Caddy. Caddy also passes the API's paths
  (`/api`, `/account`, `/connect`, `/.well-known`, `/health`, and the MCP server at `/mcp`) to the API, so the screens and the API share one address. Sign-in
  tokens are issued for that address.

`docker-compose.yml` runs both next to PostgreSQL 16. Only the screens' container is published; the API is reached through it.

## Start

```sh
cd deploy
cp .env.example .env      # then set real values; .env is never committed
docker compose up -d      # pulls the images; add --build to build them from this checkout instead
curl http://127.0.0.1:8090/health   # "Healthy"
```

Open http://127.0.0.1:8090 for the screens. Both published ports (the screens and the database) are bound to `127.0.0.1` only.
Put a reverse proxy that ends TLS in front of the screens' container, then set `AUTH_REDIRECT_URI` to `https://<that address>/signed-in`
and `FORWARDED_HEADERS=true`.

## A new customer

Gamma ERP has no public sign-up. A customer's company and its first administrator are made on the server, and that administrator makes every other user:

```sh
docker compose exec erp dotnet /app/Gamma.Erp.Api.dll create-company --username owner --country AL --company "Company name"
```

It prints the first administrator's password and first Claude token once. Give them to the administrator safely: the password must be changed at the first sign-in. To choose the password yourself, pass it as `-e ERP_ADMIN_PASSWORD='at least 12 characters'`.

Optional: `--code`, `--corporate "Group name"`, `--activity`.

A forgotten password's code goes to the address on the user's Sign-in tab, so the administrator enters their own there first, and needs `MAIL_HOST`. A user without an address asks an administrator for a new password.

## Settings (`.env`)

| Setting | What it does |
|---|---|
| `POSTGRES_PASSWORD` | The database password (required). |
| `WEB_PORT` | The screens' port on 127.0.0.1, 8090 by default. |
| `AUTH_REDIRECT_URI` | The address the screens return to after sign-in: the address people open, then `/signed-in`. |
| `AUTH_REQUIRE_HTTPS` | `true` by default. Set `false` only when a proxy in front ends TLS, or for a local test. |
| `MCP_RESOURCE` | The MCP server's public address: the address people open, then `/mcp`. Claude names it when it signs in. |
| `AUTH_SIGN_IN_LIMIT` | Tries per minute from one network address, on signing in and on the forgotten password; 10 by default. Raise it if many people sign in from one office address. |
| `FORWARDED_HEADERS` | `true` by default: the `erp` container publishes no port, so the screens' container is the API's only way in and the API trusts its `X-Forwarded-*` headers. Set `false` only if the API is reachable some other way. |
| `AUTH_SIGNING_CERTIFICATE`, `AUTH_ENCRYPTION_CERTIFICATE`, `AUTH_CERTIFICATE_PASSWORD` | The token keys: PFX files placed in `deploy/certs/`, which is mounted read-only at `/certs`. Without them, the keys last only while the process runs, so every restart signs everyone out. |
| `FILES_MAX_BYTES` | The largest file accepted, 10 MB by default. |
| `MAIL_HOST`, `MAIL_PORT`, `MAIL_SECURITY`, `MAIL_USER`, `MAIL_PASSWORD`, `MAIL_FROM` | The mail server used to send documents as PDF and forgotten password codes. Leave `MAIL_HOST` empty to keep e-mail off. `MAIL_SECURITY` is `StartTls` (the default) or `SslOnConnect`. |
| `MIGRATE_ON_START` | `true` by default. Set `false` to run the migrations yourself. |

## Data

| Volume | Holds |
|---|---|
| `db-data` | The database. |
| `erp-data` (`/data`) | The uploaded files (`/data/files`) and the keys of the sign-in cookies (`/data/keys`). |

Back up both. A file row in the database without its bytes in `/data/files` cannot be read back. The API checks every file
against the hash it saved when the file was uploaded.

## Versions

`ERP_TAG` in `.env` picks the image version: `gamma` (the latest Gamma release, the default) or an exact version such as `0.1.0-gamma`.
To update: `docker compose pull && docker compose up -d`. The API brings the database to the new schema when it starts; back up first.

## Building only the images

From the repository root:

```sh
docker build -f deploy/Dockerfile -t ghcr.io/gamma-wallet/gamma-erp-api:local .
docker build -f deploy/web.Dockerfile -t ghcr.io/gamma-wallet/gamma-erp-web:local .
```

Releases are built by `.github/workflows/release.yml`: pushing a tag `v<version>` publishes both images (amd64 and arm64) and creates the GitHub release.

# Security

## Reporting a vulnerability

Please do **not** open a public issue for a security problem.

Report it privately on GitHub: **Security › Report a vulnerability** in this repository
(<https://github.com/Gamma-Wallet/GammaERP/security/advisories/new>). Only the maintainers see the report.

Please include:

- what is affected (API, screens, MCP server, Docker images) and the version or image tag,
- the steps to reproduce it,
- what an attacker could do with it.

We aim to reply within 5 working days, keep you informed while we fix it, and credit you in the advisory unless you prefer not to be named.

## Supported versions

Gamma ERP is in its **Gamma** release stage. Fixes go into the latest release only, published as new images
(`ghcr.io/gamma-wallet/gamma-erp-api` and `ghcr.io/gamma-wallet/gamma-erp-web`, tag `gamma`). Keep your installation on the latest release.

## Running it safely

- Set your own values in `deploy/.env`; never use the example passwords.
- Keep the database port bound to `127.0.0.1` (the default) and put a reverse proxy that ends TLS in front of the web container.
- Give the sign-in keys their own certificates (`AUTH_SIGNING_CERTIFICATE`, `AUTH_ENCRYPTION_CERTIFICATE`).
- Back up the `db-data` and `erp-data` volumes.

See [deploy/README.md](deploy/README.md) for the full set-up.

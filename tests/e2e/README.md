# Gamma ERP in the browser (Playwright)

The specs drive the screens of the Docker stack (`deploy/docker-compose.yml`): the web container with the screens, the API behind it and
PostgreSQL. Each run starts on a database that is empty, as its own compose project (`gamma-erp-e2e`, the screens on
http://127.0.0.1:8091, the database on 5435), so it never touches a development database.

```sh
cd tests/e2e
npm ci
npx playwright install chromium
npm run e2e                 # builds the images, starts the stack, runs every spec, removes the stack
KEEP_STACK=1 npm run e2e    # leaves the stack running to look around (http://127.0.0.1:8091)
npm test                    # the specs only, against a stack already running (E2E_BASE_URL to aim elsewhere)
npm run lint                # type-check the specs
```

The global setup signs up the company the specs share (`e2e.owner`, test values only). Spec 01 signs up a second business through the
wizard. Failures keep a trace and a screenshot in `test-results/`; `npx playwright show-report report` opens the last report.

| Spec | What it covers |
|---|---|
| 01 sign-up | the wizard from the sign-in page, the first sign-in with the name filled in, a taken user name |
| 02 registers | a client added, found and changed with its history; a product; a row refused without its required properties; the cost centres as a tree grid; a point of sale added by its kind |
| 03 documents | a sale saved, posted, its PDF, reversed; a new product given its unit, bought and sold, the stock left, the unit in use |
| 04 configuration | a VAT group; a user made, linked to the company, signing in; a right given on a register category |
| 05 reports | a client's ledger, the VAT return and the trial balance after a sale, each as Excel and PDF |

The point of sale's own screens get their specs with each kind's screen.

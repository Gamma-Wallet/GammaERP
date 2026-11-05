# Contributing to Gamma ERP

Thank you for helping. Gamma ERP is licensed under the [AGPL-3.0](LICENSE). By contributing, you agree that your contribution is licensed under it too.

## Sign off your commits (DCO)

Every commit must carry a sign-off line certifying the [Developer Certificate of Origin 1.1](https://developercertificate.org/). It says you wrote the change, or have the right to submit it under the project's licence:

```
Signed-off-by: Your Name <you@example.com>
```

`git commit -s` adds it for you. A pull request with unsigned commits can't be merged.

## Before you send a change

- **Configuration, not code.** Business rules belong in the database configuration (document types, posting scenarios, accounts, numbering), not in C# or TypeScript. The engine only keeps the books whole: all or nothing, debit equals credit, nothing deleted, history kept.
- **Words.** Use English names in code and the database. Say *reversal* for a correcting document, never *return*. Company-currency amounts end in `Lcu`.
- **One migration.** The schema is one EF Core migration (`Initial`). Discuss a schema change in an issue first.
- **Dependencies.** Use open-source licences compatible with the AGPL only. No Telerik; no PrimeNG or PrimeUI versions under a commercial licence.
- **Secrets and data.** Never commit secrets, real customer data, or `.env` files. Use invented test data (`example.test` addresses, made-up tax numbers).
- **Tests.** Add or update tests for what you change. Run the tests of the feature you touched.
- **The guide.** If an action, a refusal or an API route changes, update its page in `.md/`.

## Reporting a security problem

Please don't open a public issue for a security problem. Write to the maintainers privately through GitHub's private vulnerability reporting on this repository.

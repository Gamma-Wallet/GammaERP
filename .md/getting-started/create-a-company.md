# Create a company

A customer's first company and its first administrator are made on the server, by hand. There is no public sign-up. That administrator then makes every other user.

**Where:** a command on the server, not a screen. **Who:** whoever runs the server.

## Actions

### Run create-company

The command is an argument of the API program. The first password comes from the setting `ERP_ADMIN_PASSWORD`, so it stays out of the shell's history. Without it, the command makes a password itself. Either way the administrator must change it at the first sign-in.

Steps, in the Docker set-up:

1. The containers run, and the database has the `Initial` migration.
2. Run:

```sh
docker compose exec -e ERP_ADMIN_PASSWORD='<at least 12 characters, or leave this out>' erp \
  dotnet /app/Gamma.Erp.Api.dll create-company --username owner --country AL --company "Company name"
```

| Option | Needed | Meaning |
|---|---|---|
| `--username` | yes | The first administrator's user name. 3 to 64 letters, digits, dots, dashes or underscores. Kept in lower case. |
| `--country` | yes | The two-letter code of a country with a starter pack. |
| `--company` | yes | The company's name. |
| `--code` | no | The company's code. Empty: the name's letters and digits, upper case, at most 12. |
| `--corporate` | no | The name of a group. Makes a group with the company under it. Without it the company is its own root. |
| `--activity` | no | An activity code. `GENERAL` by default. |
| `--bank-provider` | no | The company's open banking provider, `EnableBanking`. Without it the company has none and an administrator sets it later under Currencies and payments › Open banking. See [Open banking](../currencies-and-payments/open-banking.md). |
| `--bank-app-id` | with `--bank-provider` | The company's application id at the provider. |
| `--bank-key-file` | with `--bank-provider` | The path of the application's `.pem` file. The key is read from the file, never from the command line, and sealed. |
| `--bank-redirect` | with `--bank-provider` | Where the bank sends people back, e.g. `https://erp.example.com/bank-connected`. |
| `--bank-environment` | no | `Sandbox` (default) or `Production`. |
| `--bank-access-days` | no | 1 to 180, 180 by default. |

Countries with a pack: AL, AT, BE, BG, CY, CZ, DE, DK, EE, ES, FI, FR, GR, HR, HU, IE, IT, LT, LU, LV, MK, MT, NL, PL, PT, RO, SE, SI, SK, XK.

Activities: `GENERAL`; `RETAIL` (`SUPERMARKET`, `MINIMARKET`, `FLOWER_SHOP`); `WHOLESALE`; `SERVICE`; `HOSPITALITY` (`BAR`, `RESTAURANT`, `FAST_FOOD`); `BEAUTY` (`BARBER`); `ACCOMMODATION` (`HOTEL`). `HOSPITALITY`, `BEAUTY` and `ACCOMMODATION` sit under `SERVICE`.

Result, all in one database transaction, so it makes everything or nothing:

- A new tenant.
- The company, a cost centre of type Company, in the category Companies, with its Country and Activity properties. Its language is the first language of the country's pack.
- With `--corporate`: a group of type Corporate above it. The group gets its own books from the same pack. The group and the company become each other's partners, of the partner type "Company of the group".
- The country's pack, owned by the company: every pack group, and only what the activity's profile keeps when it has one.
- One user type, Staff.
- Two roles: Administrators, marked as administrator, and Users.
- The user `OWNER` with this user name and password, in the role Administrators, linked to the root. The password is marked to be changed (`tbl_user.must_change_password`).
- With `--bank-provider`: the company's open banking provider (`tbl_bank_provider`), the key sealed.
- The administrator's first Claude token, named "Claude (first)" (`tbl_user_token`, only its hash is kept). See [Claude tokens](../mcp/claude-tokens.md).

The command prints `Company <name> made, <n> configuration rows loaded. <user> is its administrator.`, then `Open banking: not set (…)` or `Open banking: EnableBanking, application <id>, key sealed.`, then:

- `First password: <password>`, only when it made the password itself;
- `First Claude token: GE_…`, always.

Both are shown only this once: give them to the administrator safely. The command exits with 0.

Refused when (printed as `Refused: ...`, exit code 1):

- `SetupError: a user name has 3 to 64 letters, digits, dots, dashes or underscores`.
- `SetupError: the user name <name> is taken.` User names are unique across the whole system.
- `SetupError: a password has at least 12 characters.`
- `SetupError: the company needs a name.`
- `SetupError: the country is its two-letter ISO code.`
- `SetupError: there is no starter pack for <country>; copy the configuration from another company.`
- `SetupError: The catalogue has no activity <code>.`
- Any reason of [Open banking](../currencies-and-payments/open-banking.md#set-the-provider), for example a key that is not a private key. `--bank-key-file` missing or not a file prints the usage and exits with 2.

An option missing prints the usage line and exits with 2.

API: none. The command calls `SetupService.SignUpAsync` directly.

### First steps afterwards

1. [Sign in](sign-in.md) as the administrator. The sign-in page asks you to choose your own password first.
2. Open Users and rights › Users and enter your own e-mail address on the Sign-in tab, so a forgotten password's code can reach you. See [Users](../users-and-rights/users.md).
3. Finish the company in Company › Cost centres: tax number, trade name, address, phone. See [Cost centres](../company/cost-centres.md).
4. Add cash desks, banks, warehouses and points of sale there.
5. Make the users and give them rights. See [Rights](../users-and-rights/rights.md).
6. Bring in existing data. See [Migration](../system-settings/migration.md).
7. To work through Claude, see [Gamma ERP with Claude](../mcp/README.md). The first Claude token already works.

## Related

- [Add a company to a group](add-a-company-to-a-group.md)
- [Architecture: starter packs](../architecture.md#starter-packs-and-how-a-company-is-created)

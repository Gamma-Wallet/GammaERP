# Claude tokens

A personal token lets Claude (Code, Desktop, or a script) sign in as you. With it, Claude can do only what your roles allow.

**Where:** the account menu (top right) › **Claude tokens**. **Who:** every signed-in user, for their own tokens. An administrator can also see and switch off anyone's tokens through the API (see [Users](../users-and-rights/users.md)).

Configuration: `tbl_user_token`. Each row holds the user, the token's name, the SHA-256 hash of the token, its first 7 characters, when it was made and when it was last used. The token itself is never kept.

## Actions

### Make a token

Steps:
1. Open **Claude tokens**.
2. Type a name, for example "Claude Desktop", and press **Make a token**.
3. Copy the token with **Copy**. It starts with `GE_` and is shown only this once.

Result: the token works until it is switched off. Requests that carry it (`Authorization: Bearer GE_…`) are signed in as you, with your roles and rights, exactly as on the screens. That includes the menu, categories, document types, approval limits and places. Its last use is recorded at most every five minutes.

Refused when: "A token's name has up to 60 characters."

API: `POST /account/tokens` with `name`. The answer holds `value`, the token, once.

### See your tokens

Result: name, first characters, when made and when last used. Never the token itself.

API: `GET /account/tokens`

### Switch a token off

Steps:
1. Press **Switch off** on the token.

Result: it stops working at once. Nothing is deleted.

Refused when: "There is no such token."

API: `DELETE /account/tokens/{id}`

## The first administrator's token

`create-company` makes a token named "Claude (first)" for the first administrator and prints it once, with the first password. It works at once, even before the administrator has chosen their own password. See [Create a company](../getting-started/create-a-company.md).

## Keep it safe

Treat a token like a password. A switched-off user, or one locked out by wrong passwords, can't use their tokens.

## Related

- [Gamma ERP with Claude](README.md)
- [Sign in](../getting-started/sign-in.md)

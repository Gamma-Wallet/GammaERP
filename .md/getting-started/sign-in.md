# Sign in and sign out

Signing in with a user name and a password, signing out, and changing your own password.

**Where:** the sign-in page; Account menu at the top right. **Who:** any user with a user name and a password.

## Actions

### Sign in

Steps:

1. Open the ERP's address. Without a session the browser goes to the sign-in page.
2. Type your User name and Password.
3. Press **Sign in**.

Result:

- The browser comes back to `/signed-in`, which finishes the sign-in, then to the page you asked for.
- If you have several places to work, you go to [the entry page](choose-where-you-work.md). With one, it is taken straight away.
- The sign-in is recorded and shows in [User activity](../users-and-rights/user-activity.md). A good sign-in clears the count of wrong passwords.
- The access token lives only in this browser tab and is renewed shortly before its 30 minutes end. The sign-in cookie lasts 8 hours.

User names are compared trimmed and in lower case, and are unique across the whole system. That is how the company is found.

Refused when:

- "The name or the password is wrong." The same answer for an unknown user, a user with no password, a switched-off user, and a wrong password.
- "Too many wrong passwords: try again later." 5 wrong passwords in a row lock the user for 15 minutes.
- "Too many tries. Wait a minute and try again." More than 10 tries a minute from one network address (`Auth:SignInLimitPerMinute`).
- "The sign-in did not complete. Please try again." shows on `/signed-in` when the answer does not match the sign-in that was started.

API, outside `/api/v1`:

- `GET /connect/authorize`, the code flow with PKCE, client `gamma-erp-web`
- `GET /account/login`, `POST /account/login` with the form fields `username`, `password`, `returnUrl`
- `POST /connect/token`
- `GET /account/me`, who is signed in and where they may work

A request can also be signed in with a personal Claude token instead: see [Claude tokens](../mcp/claude-tokens.md).

### Choose your own password (first sign-in)

A password someone else gave you must be changed before anything else (`tbl_user.must_change_password`). This applies to the first administrator's password from create-company, and to a password an administrator set on Users.

Steps:

1. Sign in with the password you were given. Instead of going on, the page **Choose your own password** opens.
2. Type the password you were given, then a new one twice (at least 12 characters).
3. Press **Change the password**.

Result: the new password is kept (only its hash), the mark is cleared, and the sign-in goes on as usual. Your Claude tokens keep working throughout.

Refused when:

- "The two passwords are different."
- "Choose a password different from the one you were given."
- "A password has at least 12 characters."
- The given password is wrong: back to the sign-in page with "The name or the password is wrong."

API, outside `/api/v1`: `POST /account/first-password` with the form fields `username`, `current`, `password`, `confirm`, `returnUrl`.

### Change your own password

Steps:

1. Open the Account menu at the top right, or press your name at the top of the side menu.
2. Choose **Change password**.
3. Type the Current password, the New password and New password again.
4. Press **Change**.

Result: only the new password's hash is kept, with no history copy of the old one, and any lockout ends.

Refused when:

- "The current password is wrong."
- "A password has at least 12 characters."
- "The two new passwords differ." The Change button stays off.

API: `POST /account/password` with `current` and `new`.

### Sign out

Steps:

1. Open the Account menu.
2. Choose **Sign out**.

Result: every draft you hold is let go, so others can edit it (see [Drafts](../documents/drafts.md#let-go-of-all-your-drafts)). The tokens leave this tab, the sign-in cookie ends, and the sign-out is recorded. The page says "You are signed out." with a **Sign in** button.

API: `POST /api/v1/documents/release-all`, then, outside `/api/v1`, `GET /connect/logout`.

## Related

- [Forgotten password](forgotten-password.md)
- [Choose where you work](choose-where-you-work.md)
- [Users](../users-and-rights/users.md)

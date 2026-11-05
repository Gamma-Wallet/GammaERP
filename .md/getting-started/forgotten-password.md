# Forgotten password

A forgotten password is replaced with a 6-character code sent by e-mail, never a link.

**Where:** the sign-in page › Forgot your password? **Who:** anyone, without signing in. The link shows only when the server has a mail server (`Mail:Host`).

A user without an e-mail address gets no code and asks an administrator for a new password. See [Users](../users-and-rights/users.md).

## Actions

### Ask for a code

Steps:

1. On the sign-in page press **Forgot your password?**
2. Type your User name.
3. Press **Send the code**.

Result:

- The next page always says the same: "If this user has an e-mail address, a code is on its way. It works for 15 minutes. No e-mail? Ask your administrator." It does not tell who exists.
- If the user exists and has an address, a code goes to it. Letters and digits that can't be mistaken for each other. The message is in the user's language.
- Only the code's hash is kept, in `tbl_password_reset`.
- Older codes of the user stop working.

No new code is sent within 2 minutes of the last one, so nobody can fill a mailbox. A mail failure goes to the server's log only.

API, outside `/api/v1`: `GET /account/forgot`, `POST /account/forgot` with the form field `username`.

### Use the code

Steps:

1. On the page after the request, or through **I already have a code**, fill in User name and Code.
2. Type the new password, at least 12 characters, and The same again.
3. Press **Change the password**.

Result:

- The password changes, the lockout ends and the code is closed.
- All the user's tokens are revoked, which ends their sessions.
- The page says "Password changed" and links to Sign in.

Refused when:

- "The two passwords are different."
- "The code is wrong or no longer works." A code works once, for 15 minutes, and for 5 wrong tries.
- "A password has at least 12 characters."
- "Too many tries. Wait a minute and try again." These pages share the limit of 10 tries a minute from one network address.

API, outside `/api/v1`: `GET /account/reset`, `POST /account/reset` with the form fields `username`, `code`, `password`, `confirm`.

## Related

- [Sign in and sign out](sign-in.md)

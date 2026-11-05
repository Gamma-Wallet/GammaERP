# Users

The people who sign in. There is no public sign-up: the first administrator is made on the server, and that administrator makes every other user.

**Where:** Users and rights › Users. **Who:** Write on a user category (role) to add and change a user. The tabs Sign-in and Cost centres show to administrators only. The screen and its generic actions are in [Register rows](../company/register-rows.md).

A user is a row of `tbl_user`. Its categories are its roles.

## Actions

### Add a user

Steps:

1. Choose a role on the left and press **New**.
2. Fill in the Name. The Type is Staff, made with the company.
3. In **Categories** choose the user's roles. The role chosen on the left is already there.
4. Press **Save**.
5. The tabs **Sign-in** and **Cost centres** appear. Set them as below.

Result: the user exists but can't sign in yet. They need a user name, a password and at least one cost centre.

Refused when: as in [Register rows](../company/register-rows.md).

API: `POST /users`

### Give the user a name to sign in with

Steps:

1. Open the user, then the **Sign-in** tab.
2. Type the **User name**.
3. Press **Set**.

Result: the name is kept in lower case.

Refused when:

- "A user name has 3 to 64 letters, digits, dots, dashes or underscores."
- `NameError: the user name <name> is taken.` Names are unique across the whole system, every company included, because sign-in comes before the company is known. A name taken in another company is refused with this message too.
- `NotAllowedError: only an administrator changes rights and where users work.`

API: `PUT /users/{id}/username` with `username`.

### Set the user's e-mail address

This is where a forgotten password's code goes.

Steps:

1. On the **Sign-in** tab type the **E-mail address**.
2. Press **Set**. Empty removes it.

Refused when: `"<address>" is not an e-mail address.`, or the user is not an administrator.

API: `GET /users/{id}/email`, `PUT /users/{id}/email` with `email`.

### Set the user's password

Steps:

1. On the **Sign-in** tab type the **New password**.
2. Press **Set**.

Result: only its hash is kept, and any lockout ends. Tell the user by your own means. At their next sign-in they must choose their own password first (`must_change_password`). See [Sign in](../getting-started/sign-in.md#choose-your-own-password-first-sign-in).

Refused when: "A password has at least 12 characters.", or the user is not an administrator.

API: `POST /users/{id}/password` with `new`.

### See or switch off a user's Claude tokens

An administrator sees another user's personal tokens and switches one off, for example when a laptop is lost: Users › the user › **Sign-in** tab › **Claude tokens**. Each user handles their own in the account menu, under Claude tokens: see [Claude tokens](../mcp/claude-tokens.md).

Result: a switched-off token stops working at once. Nothing is deleted.

Refused when: `NotAllowedError: only an administrator sees or switches off another user's tokens.`, or "There is no such token."

API: `GET /users/{userId}/tokens`, `DELETE /users/{userId}/tokens/{id}`.

### Choose where the user works

Steps:

1. Open the user, then the **Cost centres** tab.
2. Choose the cost centres.
3. Press **Save**.

Result: the user's links become exactly these. They choose one when they sign in. With one, it is taken straight away. They may also work at any company below a linked cost centre. See [Choose where you work](../getting-started/choose-where-you-work.md).

Refused when: `There is no cost centre <id> here.`: you can link only your node and what is below it, or the user is not an administrator.

API: `GET /users/{id}/cost-centers`, `PUT /users/{id}/cost-centers` with a list of ids.

### Change a user's roles

Steps:

1. Open the user. In **Categories** add or remove roles.
2. Press **Save**.

Result: their rights, menu and approval limit change. See [Roles](roles.md).

API: `POST /users` with `categoryIds`.

### Switch a user off

Steps:

1. Open the user and press **Switch off...**
2. Confirm.

Result: the user leaves the lists and can no longer sign in. Their history stays.

API: `DELETE /users/{id}`

## Related

- [Roles](roles.md)
- [Rights](rights.md)
- [Menu access](menu-access.md)
- [Forgotten password](../getting-started/forgotten-password.md)

The Sign-in tab shows the user's current user name and e-mail (`GET /api/v1/users/{id}/sign-in`). A user who lost every role (for example, its roles were switched off) is listed to administrators under **Without a role** below the roles; open it, give it a role and save (`GET /api/v1/users/without-category`; any register has this route, administrators only).


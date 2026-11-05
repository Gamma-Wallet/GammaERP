# User activity

Who signed in, and what each user added, changed and deleted, table by table, down to the fields of one change. Nothing extra is written for it: every version of every row already says who wrote it and when.

**Where:** Users and rights › User activity. **Who:** the `user-activity` screen in your menu, on the server too.

The first version of a row counts as Added. A switched-off row that is no history copy counts as Deleted. Any other version counts as Changed. You see rows of your node and below. Days are whole days, both included.

## Actions

### Read the sign-ins

Steps:

1. Open the screen. The period starts as the last seven days.
2. Set From, Up to and User.
3. Stay on the tab **Sign-ins**.

Result: When, User and Event: Signed in, Signed out or Wrong password. At most 500 rows, newest first.

Refused when: `NotAllowedError: User activity is not in your menu.` (403)

API: `GET /audit/users`, `GET /audit/sign-ins?from=&to=&user=`

### Read what was done

Steps:

1. Open the tab **Activity**.
2. Press a table to see its rows: When, User, Action, Row, Document. Only the latest 500 show; "Choose fewer days or one user to see the others."
3. Press a row to see its fields with Before and After.
4. When the row belongs to a document, press **Open** to go to it.

Result: nothing changes. An addition has After only, a deletion Before only. Links show the name of what they point at, never an id. A password's hash, a code's hash and the lockout counters are never shown. A row with no user reads "System".

Refused when: "There is no such table.", "There is no such row."

API:

- `GET /audit/activity?from=&to=&user=`: per table, Added, Changed and Deleted
- `GET /audit/activity/{table}?from=&to=&user=`: the versions
- `GET /audit/activity/{table}/{id}`: the fields of one version

## Related

- [Architecture: soft delete and row history](../architecture.md#soft-delete-and-row-history)
- [Register rows](../company/register-rows.md): a row's own History tab.

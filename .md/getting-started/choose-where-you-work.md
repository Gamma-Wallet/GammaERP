# Choose where you work

You work at one cost centre at a time. It decides what you see, what your menu holds and which company's configuration applies.

**Where:** the entry page "Where do you work today?"; Account menu › Change cost centre; the cost centre line at the top of the side menu. **Who:** any signed-in user.

## Actions

### Choose a cost centre

The page lists, as a tree, the cost centres linked to you and the companies below them. It does not list the cost centres inside a company.

Steps:

1. After signing in, or from **Change cost centre**, look at the list.
2. Press a row.

Result:

- The choice is remembered per user in this browser, and is taken again next time if it is still yours.
- With one place only, it is taken straight away and this page is skipped.
- Every API call now carries it in the `X-Gamma-Node` header.
- Your menu and rights are read again for that place, and you go to the home page.
- The screens follow that company's language: your choice if the company has it, else English.

Refused when:

- "You are not linked to any cost centre yet. Ask an administrator to add you to one." shows instead of a list.
- A call naming a cost centre that is neither linked to you nor below one that is answers 403: "The node in X-Gamma-Node is not one of your cost centres."
- A call with no header answers 400 when you have none linked ("You are not linked to any cost centre.") or several ("Choose the cost centre you work at with the X-Gamma-Node header.").

API:

- `GET /account/me`, outside `/api/v1`: the user, the roles and `costCenters`, each with its company, its parent in the tree, whether it is linked, the company's language and whether it is the group
- `GET /menu`: the menu at that place

An administrator links people to cost centres on the user's Cost centres tab. See [Users](../users-and-rights/users.md).

## Related

- [Add a company to a group](add-a-company-to-a-group.md)
- [Language](language.md)
- [Architecture: row-level security and tenants](../architecture.md#row-level-security-and-tenants)

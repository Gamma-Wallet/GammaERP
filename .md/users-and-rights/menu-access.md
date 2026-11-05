# Menu access

Which nodes of the company's menu a user or a role sees. The menu is also the rule on the server: a screen's routes answer only those who have that screen in their menu.

**Where:** Users and rights › Rights › view Menu. **Who:** an administrator. Only an administrator sees the whole menu.

Menu access is `tbl_inter_menu_user`: one node, for one user or one role.

## How the menu is worked out

- An administrator sees every node.
- Anyone else sees the nodes given to them or to one of their roles, everything under those, and the folders above them.
- A node marked `AdminOnly` is shown to nobody else and can't be given. In the pack: Rights and Menu.
- A documents item lists its category's document types the user was given. A new type shows with no menu change. A documents item with no type, and a folder with nothing under it, are left out.
- The company's menu is the one of the company you work at.

Until an administrator gives nodes, only administrators see the menu. The pack gives: Production point display to the role Production point, Repair board to the role Technician, Point of sale to the role Waiter, and Point of sale and Shifts to the role Cashier. See [Roles](roles.md).

## Actions

### Give menu nodes

Steps:

1. Choose **Roles** or **Users**, then the role or the user.
2. Choose the view **Menu**.
3. Tick **May see** on a node. A node under a ticked one shows ticked and can't be changed: it comes with the node above.
4. Press **Save**.

Result: the user or role holds exactly these nodes of this company's menu, and another company's menu stays as it is. The save also writes the Registers view.

Refused when:

- `NotAllowedError: this is not a node that can be given.`
- `NotAllowedError: only an administrator sees the whole menu.`

API:

- `GET /users/{id}/menu`, `PUT /users/{id}/menu`
- `GET /roles/{id}/menu`, `PUT /roles/{id}/menu`

The body of a PUT is a list of menu node ids.

### Read your own menu

API: `GET /menu`

## What the server checks

| Screen key | Routes it opens |
|---|---|
| `report` with its parameter | `/reports/*`, `/posting/unbalanced-scenarios`, the ledgers, `/statistics/partners`, with their PDF and Excel |
| `dashboard` | `/dashboard` |
| `user-activity` | `/audit/*` |
| `migration` | `/migration/*` |
| `files` | `/files/*` |
| `currencies` | reading `/currencies` and `/exchange-rates`. Changing them is for administrators only |
| `pos` | `/pos/*` |
| `pos-shifts` | `/pos-shifts/*` |
| `production-display` | `/production-display/*` |
| `production` | `/production/*` |
| `stock-convert` | `/stock/*` |
| `lots`, `serials` | parts of `/lots` and `/serials` |
| `repairs` | `/repairs/*` |
| `bank-statements` | `/bank-statements/*` |
| `approvals` | `/approvals` |
| `invoice-run`, `renewals` | parts of `/contract-billing` |
| `documents` | a contract's billing plan, read and set: `GET`, `PUT /contract-billing/{id}` |
| `document-types` | saving document categories and types: `POST /documents/categories`, `POST /documents/types` |
| `setup` with the register's name | saving a register's categories and types, and copying properties: `POST /{register}/categories`, `POST /{register}/types`, `POST /{register}/types/{id}/properties/copy`. Transaction types take `entities` with `transactions` instead. Opportunities have no Set up item, so only administrators |
| `entities` with `partners`, the `partner-statistics` report, or any `documents` screen | `/partners/{id}/statistics` |
| `opportunity-board` | parts of `/opportunities` |
| `year-closing` | `/year-closing` |
| `depreciation`, `asset-value` | `/depreciation/*` |

A route refused this way answers 403: `NotAllowedError: this screen is not in your menu.` An administrator passes.

A register's rows are not checked by the menu. They follow rights per category. Configuration rows can be read by any signed-in user of the company, except the menu rows, and only an administrator writes them. See [Configuration screens](../system-settings/configuration-screens.md).

## Related

- [Rights](rights.md)
- [Roles](roles.md)
- [Menu](../system-settings/menu.md): changing the tree itself.

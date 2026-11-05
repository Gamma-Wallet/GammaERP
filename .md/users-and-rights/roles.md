# Roles

A role is a user category (`tbl_user_category`). A user can hold several. A user's rights are those given to the user and to each of their roles, added together.

**Where:** the left side of Users and rights › Users shows them. They are changed in Users and rights › Roles, and also in System settings › Types and categories › Users and roles. **Who:** Users and rights › Roles is for administrators only: the item shows in no one else's menu. For Types and categories see [Types and categories](../system-settings/types-and-categories.md).

## The roles a company starts with

From the starter pack:

| Role | Code | Given by the pack |
|---|---|---|
| Administrator | `ADMINISTRATOR` | Is administrator ticked: its users are administrators |
| Finance | `FINANCE` | nothing |
| Seller | `SELLER` | nothing |
| Warehouse keeper | `WAREHOUSE` | nothing |
| IT | `IT` | nothing |
| Consultant | `CONSULTANT` | nothing |
| Production point | `PRODUCTION_POINT` | See [Production point](#production-point) |
| Technician | `TECHNICIAN` | See [Technician](#technician) |
| Waiter | `WAITER` | See [Waiter](#waiter) |
| Cashier | `CASHIER` | See [Cashier](#cashier) |

What the pack gives a role is rows in three tables: menu items (`tbl_inter_menu_user`, file `InterMenuUser.json`), document types (`tbl_inter_document_type_user`, file `InterDocumentTypeUser.json`) and Write on document or partner categories (`tbl_elements_access`, file `ElementsAccess.json`). They are proposals for every new company: an administrator changes them afterwards in Users and rights › Rights and Roles, and on the Users tab of System settings › Document types.

Made by `create-company`:

| Role | Code | Notes |
|---|---|---|
| Administrator | `ADMINISTRATOR` | From the pack, marked `IsAdministrator`. The first user holds it. |
| Users | `USERS` | From the pack. A further user made by `create-company` who is not an administrator holds it. |

Both roles and the user type Staff (`STAFF`) come from the starter pack: the company owns them and they carry its language's names. A pack without them gets them made with English names, still owned by the company.

Every other role (Finance, Seller, Warehouse keeper, IT, Consultant, Users) sees nothing until an administrator gives it menu nodes and rights.

### Administrators

A role marked `IsAdministrator` gives every right, still only at the user's node and below. Only an administrator:

- changes configuration rows;
- changes rights, user names, passwords, e-mail addresses and where users work;
- sees the whole menu, Rights and Menu;
- gives document types to users.

### Production point

For the people at a kitchen, bar or oven. The pack gives:

- the menu item Point of sale › Production point display, the screen where they say a line was produced;
- the document type Production;
- Write on the document category Production.

Give the user the role and link them to their place. Produced on a product with a recipe then works, and puts the product into the point of sale that ordered it, although the cook works only at the production point. See [Production point display](../point-of-sale/production-point/display.md). To do more, give the role more in [Menu access](menu-access.md).

### Technician

For the people at a repair shop. The pack gives:

- the menu item Repairs › Repair board;
- the document types Repair intake, Repair offer and Sales invoice, services;
- Write on the document categories Repairs and Sales, and on the partner category Clients, because New job makes the client.

With that a technician runs a job from New job to a posted invoice. See [Repairs](../repairs/README.md).

The technician shown on a repair job is an employee, not a user. The board's Technician list reads [Employees](../company/employees.md). So a person may need both: a user with this role to sign in, and an employee to be named on jobs.

### Waiter

For the people serving tables. The pack gives:

- the menu item Point of sale;
- the document types Point-of-sale order, Point-of-sale receipt and Point-of-sale receipt of money;
- Write on the document categories Orders, Sales and Cash desk.

A waiter sends an order, bills the table and takes the payment. A purchase invoice is refused: its type is not given to the role. See [Point of sale](../point-of-sale/overview.md).

### Cashier

As Waiter, and also:

- the menu item Point of sale › Shifts;
- the document type Point-of-sale payment, for money out of the cash desk.

See [Shifts](../point-of-sale/shifts.md) and [Money in and out](../point-of-sale/money-in-out.md).

## Actions

### Add a role, change it, or make it an administrator role

The configuration screen Users and rights › Roles (`tbl_user_category`, model `user-roles`). Its generic actions are in [Configuration screens](../system-settings/configuration-screens.md).

| Field | Column | Meaning |
|---|---|---|
| Code | `Code` | The role's code. |
| Name | `Name` | The role's name. |
| Is administrator | `IsAdministrator` | Its users are administrators. See [Administrators](#administrators). |
| Sort order | `SortOrder` | Its place in lists. |

Steps:

1. Open Users and rights › Roles.
2. Press **Add**, or press a row.
3. Fill in Code and Name. Tick **Is administrator** to give administrator power to the role's users. Set the Sort order.
4. Press **Save**.

Result: the role can be chosen on a user, in Rights, on a document type and in Approval limits. With Is administrator ticked, every user who holds the role is an administrator. Unticked, they are not, unless another of their roles has it.

Refused when: as in [Configuration screens](../system-settings/configuration-screens.md).

API: `GET /config/user-roles`, `POST /config/user-roles`, `DELETE /config/user-roles/{id}`

### Add or change a role in Types and categories

This editor has no Is administrator box.

Steps:

1. Open System settings › Types and categories › Users and roles.
2. Under Categories press **Category**, or **Edit** on a role.
3. Fill in the Name and Code, and keep **Holds rows** ticked.
4. Press **Save**.

Result: the role can be chosen on a user, in Rights, on a document type and in Approval limits.

Refused when: as for a category in [Types and categories](../system-settings/types-and-categories.md).

API: `GET /users/categories`, `POST /users/categories`

### Give a role to a user

See [Users](users.md).

### Give a role its rights

See [Rights](rights.md) and [Menu access](menu-access.md).

## Related

- [Users](users.md)
- [Rights](rights.md)
- [Menu access](menu-access.md)
- [Approval limits](approval-limits.md)


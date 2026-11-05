# Employees

The company's staff as a register. An employee is not a user: a user signs in, an employee is a person the books and jobs name. HR is out of scope, so there is no payroll here.

**Where:** Company › Employees. **Who:** read and Write on the category of `tbl_employee`. See [Rights](../users-and-rights/rights.md). The screen and its generic actions are in [Register rows](register-rows.md).

From the starter pack:

| | Rows |
|---|---|
| Categories | Finance, Staff |
| Types | Administrator, Financier, Warehouse keeper, Driver, Worker |

The pack gives these types no properties. Add them in System settings › Types and categories › Employees.

## Actions

### Add an employee

Steps:

1. Choose a category and press **New**.
2. Choose the Type and fill in the Name.
3. Press **Save**.

Result: the employee can now be picked:

- as a technician on a repair job;
- on a staff line (the transaction type Staff) of a document;
- as a Warehouse's Authorised person;
- in the Staff ledger report.

Refused when: as in [Register rows](register-rows.md).

API: `POST /employees`

### Change or switch off an employee

See [Register rows](register-rows.md).

API: `POST /employees`, `DELETE /employees/{id}`

### Read an employee's ledger

The report Staff ledger, under Reports › Financial.

Who: the report `staff-ledger` in your menu.

API: `GET /employees/{id}/ledger?from=&to=`

### Bring in staff from a file

The Migration step Staff, kind Elements. See [Migration](../system-settings/migration.md).

## Employees and users

Employees (the staff register) and users (who sign in) are separate and stay separate: the founder decided on 2026-11-03 not to link them. Where a person works when they sign in comes from the user's cost centres (Users and rights). The table `tbl_inter_employee_cost_center` exists but has no screen and nothing reads it.

## Related

- [Register rows](register-rows.md)
- [Types and categories](../system-settings/types-and-categories.md)
- [Roles](../users-and-rights/roles.md): the role Technician.


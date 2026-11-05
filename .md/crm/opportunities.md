# Opportunities

A sale being worked on with a client or a lead: who, how much, when, and at which stage.

**Where:** CRM › Opportunities. **Who:** the menu item in your menu (Users and rights › Rights); the read or write right on the opportunity's category.

It is a register like the others: categories on the left, rows in the middle, the open row on the right. The base pack gives the category Sales opportunities and the type Opportunity, code OPP.

## Actions

### Add an opportunity

Steps:
1. Click the category, then New. The button shows only with the write right on that category.
2. Fill Details:
   - Code. Empty takes the type's next code.
   - Type, Under, Name, Description, Categories.
   - Client or lead. A partner.
   - Stage.
   - Amount, Currency. The company's active currencies, the base one first.
   - Expected close. Without it, the opportunity is left out of the forecast.
   - Owner. The user working on it.
3. Click Save.

Result: the opportunity shows on the Pipeline in its stage. No account, stock or balance changes.

Refused when:
- No client or no stage: "An opportunity needs a client and a stage."
- A stage whose outcome is lost and no Reason it was lost: "A lost opportunity needs the reason it was lost."
- No name: "A name is required."
- No category: "An element is in at least one category."
- No write right on any of its categories: "You may not write elements of this category."
- The same type, name and category at this level: NameError.
- The type has used its code range: CodeLimitError.

API: `POST /opportunities` with `opportunity: { partnerId, stageId, amount, currencyId, expectedClose, ownerUserId, lostReason }`. The form's stages and currencies: `GET /opportunities/form`.

### Change an opportunity

Steps:
1. Click the row.
2. Change it. The stage can be changed here too. A lost stage shows "Reason it was lost".
3. Click Save.

Result: the old state is kept as history.

Refused when: as for adding.

API: `POST /opportunities` with its `id`. Read one with `GET /opportunities/{id}`.

### Find an opportunity

Steps: click a category, type in "Search by code or name", or click a type chip.

API: `GET /opportunities?category={id}&type={id}&search={text}&page=1&pageSize=50`

### Give it to chosen users or roles

Steps: open the row, tab Access, choose users and roles, click Save.

Result: nobody chosen means everyone with the right to the category sees the row. Otherwise users with only "read own" see it when they or their role are chosen. The [Pipeline](pipeline.md) and the [Forecast](forecast.md) follow the same rule.

API: `POST /opportunities` with `accessUserIds` and `accessRoleIds`.

### See its documents

Steps: open the row, tab Documents. It lists the offers, orders, contracts and other documents linked to it that you may read. See [opportunity on documents](opportunity-on-documents.md#see-an-opportunitys-documents).

API: `GET /opportunities/{id}/documents`

### See its history

Steps: open the row, tab History. Every stage move is in the row's history.

API: `GET /opportunities/{id}/history`

### Switch an opportunity off

Steps: open the row, click Switch off…, confirm.

Result: it leaves the lists, the Pipeline and the Forecast. Nothing is deleted.

Refused when: rows sit under it (ChildrenExistError), or you have no write right on its categories.

API: `DELETE /opportunities/{id}`

### Set up categories and types

Steps: open CRM › Opportunity types and categories, or click Set up on the register. It works as for products.

Result: see [product types and categories](../products/product-types-and-categories.md). No VAT group, tracking or Units parts.

Refused when: saving a category or a type needs CRM › Opportunity types and categories in your menu, checked on the server: `NotAllowedError: this screen is not in your menu.` Administrators pass. The Set up button shows for an administrator, or for anyone with the write right on some opportunity category.

API: `GET /opportunities/categories`, `POST /opportunities/categories`, `GET /opportunities/types`, `POST /opportunities/types`

## Related

- [Pipeline](pipeline.md)
- [Forecast](forecast.md)
- [Opportunity stages](opportunity-stages.md)
- [Partners](../partners/partners.md)

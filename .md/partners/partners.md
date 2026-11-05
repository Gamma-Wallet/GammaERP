# Partners

The register of clients, suppliers and leads. A partner can be both client and supplier: it is listed in each category it belongs to.

**Where:** Clients and suppliers. **Who:** the menu item in your menu (Users and rights › Rights); the read or write right on the partner's category.

The screen has three parts: categories on the left, the category's rows in the middle, the open row on the right. The base pack gives the categories Clients, Suppliers and Leads.

## Actions

### Find a partner

Steps:
1. Click a category.
2. Type in "Search by code or name", or click a type chip.

Result: the matching rows, 50 to a page, or a tree when rows sit under one another and there are at most 2000.

Refused when: you have no read right on any category. The screen says "No category is open to you."

API: `GET /partners?category={id}&type={id}&search={text}&page=1&pageSize=50`

### Add a partner

Steps:
1. Click the category, then New. The button shows only with the write right on that category.
2. Fill the Details tab:
   - Code. Empty takes the type's next code.
   - Type. Unknown, Person, SME, LTD or Company of the group in the base pack.
   - Under. Another partner, or empty for the top.
   - Name, Description.
   - Categories. Add Suppliers as well for one that is both.
   - Payment days. See [payment days](payment-days.md).
   - Properties. In the base pack: Tax number, City, Address, Phone and E-mail.
3. Click Save.

Result: the partner can be chosen on documents. No balance changes.

Refused when:
- No name: "A name is required."
- No category: "An element is in at least one category."
- No write right on any of its categories: "You may not write elements of this category."
- The same type, name and category at this level: NameError.
- The type does not fit the parent: TypeError.
- The type has used its code range: CodeLimitError.
- A required property is empty, or a value does not fit its kind.

API: `POST /partners` with no `id`.

### Change a partner

Steps: click the row, change it, click Save.

Result: the old state is kept as history. Documents already made keep the name they were saved with.

Refused when: as for adding, and when another row has the same name and description at this level (NameError).

API: `POST /partners` with its `id`. Read one with `GET /partners/{id}`.

### Turn a lead into a client

Steps: open the partner and, in Categories, add Clients and remove Leads. Click Save.

Result: the partner is listed under Clients. Nothing does this by itself, not even a won opportunity.

API: `POST /partners` with `categoryIds`.

### Give a partner to chosen users or roles

Steps: open the row, tab Access, choose users and roles, click Save.

Result: nobody chosen means everyone with the right to the category sees the row. Otherwise users with only "read own" see it when they or their role are chosen.

API: `POST /partners` with `accessUserIds` and `accessRoleIds`.

### See its history

Steps: open the row, tab History.

API: `GET /partners/{id}/history`

### Switch a partner off

Steps: open the row, click Switch off…, confirm.

Result: it leaves the lists and can no longer be chosen. Nothing is deleted, and its balance rows stay.

Refused when: rows sit under it (ChildrenExistError), or you have no write right on its categories.

API: `DELETE /partners/{id}`

### Load partners from a file

Steps: use the Migration menu, steps PARTNER_CATEGORIES, PARTNERS and OPENING_PARTNERS.

API: `POST /migration/{code}/import`

## Related

- [Partner types and categories](partner-types-and-categories.md)
- [Payment days](payment-days.md)
- [General client](general-client.md)
- [Balances](balances.md)
- [Opportunities](../crm/opportunities.md)

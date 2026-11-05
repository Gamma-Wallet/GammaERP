# Types and categories

The categories, types and properties of a register. One set-up view serves them all.

**Where:** System settings › Types and categories › Clients and suppliers, Employees, Products and services, Cost centres, Units of measure, Users and roles; CRM › Opportunity types and categories. The same view opens with **Set up** on a register's own screen, and for Transaction types. Chart types and categories is under Accounting setup. **Who:** the screen offers it to an administrator, or to whoever may write some category of the register.

**Checked on the server:** saving a category or a type, and copying properties, needs the register's Set up item in your menu (screen `setup` with the register's name, for example System settings › Types and categories › Clients and suppliers). Transaction types need their own register screen, System settings › Transaction types. For opportunities it is CRM › Opportunity types and categories. Administrators pass. Otherwise: `NotAllowedError: this screen is not in your menu.` Reading categories and types stays open to the screens that use them.

`{register}` below is `partners`, `employees`, `products`, `cost-centers`, `units`, `users`, `transactions`, `charts` or `opportunities`.

The left table is the categories, the right table the types, both as trees. Pressing a category filters the types; press it again, or **All types**, for all of them.

A category or a type is a folder or holds rows. Folders sort the tree.

## Actions

### Add or change a category

Steps:

1. Press **Category**, or **Edit** on a row.
2. Fill in the Name and the Code, and choose Under.
3. Tick **Holds rows**. Otherwise it is a folder of categories.
4. Press **Save**.

Result: the category is saved for the cost centre you work at, or changed in place.

Refused when:

- "A name is required."
- "The category does not exist."
- `NameError: a category with this name is already at this level.`
- `ElementsExistError: the category has elements; it cannot be moved.`
- "A category cannot be placed under itself or under one below it."

API: `GET /{register}/categories`, `POST /{register}/categories`

### Switch a category off

Steps:

1. Edit it and press **Switch off...**
2. Confirm.

Result: it leaves the tree. Nothing is deleted.

Refused when: `ChildrenExistError: the category has active categories under it.`

API: `POST /{register}/categories` with `active: false`.

### Add or change a type

Steps:

1. Press **Type**, or press a row.
2. Fill in:
   - Name and Code.
   - Under.
   - Codes from and Codes to: rows of the type get the next code, as the type's Code plus the number.
   - Categories: the categories whose rows may have this type.
   - **Rows can have this type**. Otherwise a folder of types.
3. For products also: VAT group, Tracks serial numbers, Tracks lots, An expired lot can't go to a client, "Fixed asset: its products are registered and depreciated" (`fixed_asset`, set on each type, not taken from the type above), and Units. See [Product types](../products/product-types-and-categories.md). For cost centres also: "Bank accounts: bank statements are uploaded for them" (`TakesBankStatements`) and "Production points: they have a production point display" (`IsProductionPoint`). See [Cost centres](../company/cost-centres.md).
4. Add its properties, below.
5. Press **Save**.

Result: the type and its properties are saved.

Refused when:

- "A name is required."
- "The type does not exist."
- `NameError: a type with this name is already at this level.`
- `CodeLimitError: the next code must lie between the first and the last.`
- "A category does not exist."
- `NameError: the property <name> is there twice.`
- "A type cannot be placed under itself or under one below it."

API: `GET /{register}/types`, `POST /{register}/types`

### Add a property to a type

A property is a field rows of the type have (`tbl_*_type_info`).

Steps:

1. In the type editor press **Property**.
2. Fill in the Name and choose the Kind:

| Kind | Value |
|---|---|
| Text | A text per language. |
| Number | An exact decimal. |
| Date; Date and time | A date; a date and time. |
| List (one choice); List (several choices) | Its choices. |
| Choices that show more fields | Choices, each able to show other properties. |
| Lookup (a row of a register) | A row, chosen from **Picks from**. |
| Several texts | Several texts, each per language. |
| Location on a map | A latitude and longitude. |
| Photo | The link to a stored file. Choosing a file on a form stores it at once (`POST /api/v1/files`, PNG, JPEG, WebP or GIF); the row keeps its link when it is saved. A photo can be changed whenever the row is edited. |

3. Fill in **Code** if the system must read this property (hint: "A key the system reads, e.g. BARCODE. Empty for most"). It is saved in upper case, in the column `code`. See [Property codes the system reads](#property-codes-the-system-reads).
4. For a list kind, press **Choice** and type each choice's name, one box for English and one for the company's own language. A name left empty in one language is copied from the other on save.
5. Tick **Required** if it must be filled in.
6. In **Shown** choose "Always shown", or a saved choice of a Choices that show more fields property of this type.
7. Move it with the arrows, or remove it.
8. Press **Save** on the type.

Result: the form of rows of this type shows the property. A removed property or choice is switched off. A shared type's properties of another company stay as they are.

Refused when:

- "A name is required." on the property, or "A choice needs a name."
- "Unknown kind."
- "The property does not exist."
- "It must be a saved choice of another activating list of this type." A list and the properties it shows are saved in two steps, since a new choice gets its id when saved.

API: `POST /{register}/types` with `properties`.

### Property codes the system reads

The engine finds the properties it reads by their **Code**, never by their name. A property can be renamed and translated freely. The starter packs set these codes, so a new company has them.

| Code | Register | What reads it |
|---|---|---|
| `BARCODE` | Products | The product's barcode: scanning at the point of sale. |
| `PHONE` | Clients and suppliers | The partner's phone: the repair client search, and making a repair client. |
| `TAX_NUMBER` | Clients and suppliers | The partner's tax number in the VAT books and reports. |
| `CITY` | Clients and suppliers | The partner's city in the VAT books and reports. |
| `NEGATIVE_STOCK` | Cost centres (Company) | Negative stock allowed. See [Negative stock](../stock/negative-stock.md). |
| `HOLD_TIMEOUT` | Cost centres (Company) | The hold timeout of drafts, in minutes. See [Drafts](../documents/drafts.md). |
| `COUNTRY` | Cost centres (Company) | The company's country, used by posting. |
| `TRADE_NAME`, `TAX_NUMBER`, `ADDRESS`, `PHONE`, `EMAIL` | Cost centres (Company) | The places under the company's name in the header of every PDF, in this order; the label is the property's name, the value printed when set. |
| `LOGO` | Cost centres (Company) | The company's logo (a Photo, PNG or JPEG): printed at the top of every PDF the company prints, above its name. Excel exports stay without it. |

Clearing or changing a code stops the system reading that property.

### Copy properties from another type

Steps:

1. In a saved type's editor choose **Copy from a type...**
2. Press **Copy**.

Result: the other type's properties come over, with their choices and links. A property the type already has, by name, is left as it is.

Refused when: "The type to copy from does not exist.", "A property is not one of the type to copy from."

API: `POST /{register}/types/{id}/properties/copy` with `fromTypeId` and optional `propertyIds`.

### Switch a type off

Steps:

1. Open the type and press **Switch off...**
2. Confirm.

Result: "New rows can no longer have this type. Nothing is deleted."

Refused when: `ChildrenExistError: the type has active types under it.`

API: `POST /{register}/types` with `active: false`.

## Related

- [Register rows](../company/register-rows.md)
- [Roles](../users-and-rights/roles.md)
- [Transaction types](transaction-types.md)

## Open points

- Lookup scopes are rows of `tbl_elements`. `GET /lookups` lists them, and they have no screen of their own.

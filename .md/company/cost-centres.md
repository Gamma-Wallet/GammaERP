# Cost centres

The organisation as one tree: the group, its companies, and their cash desks, banks, warehouses, offices and points of sale. Every row in the ERP belongs to a cost centre.

**Where:** Company › Cost centres. **Who:** read and Write on the Companies category of `tbl_cost_center`. See [Rights](../users-and-rights/rights.md). The screen and its generic actions are in [Register rows](register-rows.md).

## The types

These come from the starter pack (`tbl_cost_center_type`). Corporate and Company are the organisation's own.

| Type | Under | Notes |
|---|---|---|
| Corporate | top | The group. |
| Company | Corporate, or top for a single company | Keeps its own books (`KeepsBooks`). |
| Base | Company | |
| Cash desk | Company | |
| Bank | Company | Takes bank statements (`TakesBankStatements`). |
| Warehouse | Company | |
| Office | Company | |
| Production point | Company | Has a production point display (`IsProductionPoint`). |
| Point of sale | Company | A folder of types. Its kinds are below. |
| Restaurant / café | Point of sale | Places: Hall, then Table; Counter. |
| Shop | Point of sale | Places: Till. |
| Repair shop | Point of sale | |
| Cinema / theatre | Point of sale | Places: Auditorium, Row, Seat. |
| Hotel | Point of sale | Places: Floor, Room. |

The flags `KeepsBooks`, `TakesBankStatements` and `IsProductionPoint` are columns of the type. The type editor does not show `KeepsBooks`. It shows the other two.

### Mark a type as a bank or a production point

Steps:

1. On Cost centres press **Set up**, then press the type, or **Type** for a new one.
2. Tick "Bank accounts: bank statements are uploaded for them" (`TakesBankStatements`) or "Production points: they have a production point display" (`IsProductionPoint`), or both.
3. Press **Save**. The boxes are saved with the type.

Result: cost centres of a type with the first box can be picked as the Bank under Documents › Bank statements. Cost centres of a type with the second box are production points and have a production point display. Untick a box to take this away.

Refused when: as in [Types and categories](../system-settings/types-and-categories.md#add-or-change-a-type).

API: `POST /cost-centers/types`

## The properties

| Type | Properties (`*` required) |
|---|---|
| Company | Country*, Activity*, Tax number, Trade name, Address, Phone, E-mail, Website, Logo, Negative stock allowed (cost centres), Negative stock allowed (partners), Negative stock allowed (employees), Hold timeout (minutes), Fiscal year starts (month). In a group also Ownership % and Consolidation method. |
| Base | Owner, Phone |
| Cash desk, Bank | Name*, Currency*, Account* |
| Warehouse | Address, Phone, Authorised person |
| Office | Address |
| Restaurant / café, Shop, Repair shop, Cinema / theatre, Hotel | Address*, Phone*, E-mail* |
| Table, Seat | Position X, Position Y. A Table also has Seats. |
| Room | Beds |

They are rows of `tbl_cost_center_type_info`, changed in [Types and categories](../system-settings/types-and-categories.md).

## Actions

### Finish the company

Steps:

1. Press the company in the tree.
2. Fill in its properties, such as Tax number, Trade name and Address.
3. Press **Save**.

Result: the values are kept on the company. Printed documents show the company's name.

API: `POST /cost-centers`

### Add a cash desk, bank, warehouse or office

Steps:

1. Press **New**.
2. Choose the Type, and in **Under** choose the company.
3. Fill in the Name and the type's properties. A cash desk and a bank need their Currency and their Account.
4. Press **Save**.

Result: a new cost centre under the company. A user or a document can now use it. A bank can take bank statements.

Refused when: as in [Register rows](register-rows.md), and:

- `CompanyLevelError: a company is the root of its tree or directly under it, never under another company.`

API: `POST /cost-centers`, and `GET /lookups/{scope}` for the Account and Currency choices.

### Add a point of sale

See [Points of sale](points-of-sale.md).

### Add a company

Not from this screen. See [Add a company to a group](../getting-started/add-a-company-to-a-group.md).

### Read a cost centre's balance

Reads what a cost centre stands at. No screen in this menu calls it.

API: `GET /cost-centers/{id}/balance?until=`

The Cost centre ledger is a report under Reports › Financial.

### Change the company's logo

Steps:
1. Open the company under Company › Cost centres.
2. In **Logo**, choose an image file (PNG or JPEG for prints).
3. Press **Save**.

Result: the image is stored as a file and the company's Logo keeps its link. From then on every PDF of the company (documents and reports) shows it at the top left, above the company's name. It can be changed at any time the same way. Excel exports stay without it.

Refused when: the file is larger than the store allows, or of a type it does not take.

API: `POST /api/v1/files` (the image), then the company's save with the Logo property set to the file's link.

### What a print shows of the company

Every PDF the company prints (documents and reports) has the same header and footer:

- **Header:** the logo when it is set, the company's name, then always the same five places: Trade name, Tax number, Address, Phone and E-mail, each with its value when the company has set it. The labels are the properties' own names in the print's language.
- **Footer:** "Powered by Gamma Wallet · www.gamma-wallet.com" on the left, the page number on the right.

The places are the Company type's properties with the codes `TRADE_NAME`, `TAX_NUMBER`, `ADDRESS`, `PHONE` and `EMAIL` (see [Property codes the system reads](../system-settings/types-and-categories.md)). Excel exports stay without them.

### What you see

You see cost centres you work at and those below them, whoever made them. So a bar can arrange and add the tables below it, although the company made them, given the Write right. A group does not see its companies' cost centres.

## Related

- [Register rows](register-rows.md)
- [Points of sale](points-of-sale.md)
- [Choose where you work](../getting-started/choose-where-you-work.md)
- [Types and categories](../system-settings/types-and-categories.md)
- [Migration](../system-settings/migration.md): the step "Warehouses, banks, cash desks and places".


# MCP tools

Every tool of the Gamma ERP MCP server. Each one does what its screen does, through the same service, and checks the same right:
- the menu screen or report, refused with "this screen is not in your menu.";
- administrator only, where the screen is.

The services check the rest: categories, document types, approval limits, places. A refusal reaches Claude in the ERP's own words.

**Where:** the server at `/mcp`. Code: `src/backend/Gamma.Erp.Api/Mcp/Tools/`, `GuideTools.cs`. **Who:** the signed-in user, with their rights.

Every **changing** tool takes `confirm`. Without `confirm=true` it changes nothing and answers "Not done: show the user … and call again with confirm=true after their explicit yes." Posting and reversing are marked as final (destructive) to Claude.

Lists come in pages of 25 by default, 100 at most.

## Where you work and the guide

| Tool | Does | Changes |
|---|---|---|
| `list_workplaces` | Who is signed in, whether an administrator, and the places they may work at, marking the chosen one | no |
| `choose_workplace` | Chooses the place to work at; refused for a place that isn't the user's | no (the choice only) |
| `search_guide` | Searches this user guide; the best pages with an excerpt | no |
| `read_guide` | One guide page by its path; `README.md` is the index | no |

## Finding things

| Tool | Does | Right |
|---|---|---|
| `find_partners`, `find_products`, `find_accounts`, `find_cost_centers`, `find_opportunities` | Search by code or name through the register's own lookup; id, code, name, type, categories (opportunities also client, stage, amount, currency, expected close) | the register's rights |
| `document_types` | The categories and the document types the user may write; with a category or type, each type's form: sides, line kinds and their fields, payment methods, VAT groups, currencies, properties | document type rights |

## Documents

| Tool | Does | Changes |
|---|---|---|
| `list_documents` | One category's documents, newest first: type, posting period, posted or draft, search; totals in LCU | no |
| `get_document` | Header, lines, totals, the documents it came from and those made from it; on a posted one, what is left to reverse per line; on a sheet, its rows (account, place, debit, credit, description) with the sheet's debit and credit totals | no |
| `next_documents` | What the document can make (its flows), with the refusal reasons, and what was already made | no |
| `save_draft_document` | Makes a draft or changes one: type, dates, sides, lines. A line's unit, price, VAT group and kind are proposed as in the editor when left out | yes |
| `make_next_document` | Makes the next document from a flow, as **Generate** | yes |
| `post_document` | Posts a draft: final; books what its type books | yes, final |
| `reverse_document` | Reverses a posted document, whole or by line quantities, on a date | yes, final |
| `save_journal_sheet` | Makes or changes a draft journal sheet (general, recurring, any type whose rows are journals), as the sheet's grid does: each row one journal under the sheet, with its row type, kind of line, account (or partner, product), debit or credit, place and description. On a change, row n changes the sheet's row n, as in the grid; rows past the given ones are switched off. Debit = credit is checked over the whole sheet when it is posted with `post_document` | yes |
| `repeat_document` | Does a document or sheet again on another day, as **Repeat**: a copy dated that day, linked to the original; posted at once only with `post=true`. Started by a person each time | yes, final when posted |

Not covered: serial numbers on a reversal (whole or by quantity only); on a sheet, Payment and Receipt rows that name the invoices they cover (use the Payment and Receipt documents, or the sheet screen).

## Reports

`trial_balance`, `balance_sheet`, `profit_and_loss`, `account_ledger`, `open_items`, `stock`, `sales_lines`, `vat_return`, `forecast`, `dashboard`. Each needs its report in the user's menu, as on the screens.
- `trial_balance` leaves out rows that are all zero.
- `vat_return` without categories gives the configured return form; with output and input categories, the return by category.

## CRM, currencies, configuration, skins

| Tool | Does | Changes |
|---|---|---|
| `pipeline` | The board: stages and cards | no |
| `move_opportunity` | Moves an opportunity to a stage (by name or id); a lost stage needs the reason | yes |
| `list_currencies`, `exchange_rates` | The company's currencies; a currency's rates in a period | no |
| `set_exchange_rates` | Saves one or many rates (currency, day, rate: one unit in the company's own currency) in one transaction: all or none | yes |
| `read_configuration` | The configuration models, or a model's rows with filters and its columns; needs that configuration screen | no |
| `save_configuration_row` | Saves a configuration row, e.g. payment methods by cost centre, point-of-sale settings, closed periods; administrators | yes |
| `list_skins`, `use_skin` | The company's skins; puts one in use; administrators | `use_skin` yes |

## Setting up a company (administrators)

| Tool | Does |
|---|---|
| `add_company_to_group` | A new company in the group, from its country's pack (as **New company**); refused for a single company with no group |
| `set_company_details` | The company's properties by their codes (TRADE_NAME, TAX_NUMBER, ADDRESS, PHONE, EMAIL…); an unknown code is refused with the list of the company type's properties |
| `add_cost_center` | A warehouse, cash desk, bank, point of sale, or a place under one (a till, a table): type, parent, name, category, property values |
| `add_user` | A user with user name, e-mail, places, roles and a first password, which they must change at their first sign-in; all in one transaction |
| `set_user_access` | Replaces a user's places and/or roles |

All five are changing tools. Opening balances and stock still come in on the Migration screen, which needs a file. A new organisation is made only on the server (`create-company`).

## Registers: clients, suppliers, products, accounts, units, places

| Tool | Does | Changes |
|---|---|---|
| `register_setup` | A register's categories, its types with their categories and properties (code, kind, required, choices); for products also the VAT groups and units there are, and a type's units | no |
| `save_register_row` | Adds or changes a row of a register as its screen does: a partner (client or supplier), product or service, account, unit or cost centre. Type, categories, name (in a language), code, parent, description, property values by code; partners also payment days; products the default VAT group, composite, and their units in order (the first the default). On a change only what is given changes | yes |
| `switch_off_register_row` | Switches a row off, as the screen's delete; refused while active rows sit under it, and for the general client | yes |
| `save_register_category` | Adds or changes a register's category (holds rows or only groups); needs the register's **Set up** screen | yes |
| `save_register_type` | Adds or changes a register's type: name, code, parent type, categories, folder, properties (given ones added or changed by code or name, the others kept), code range; product types also VAT group, lots, serials, fixed asset, units; cost-centre types bank statements, production point. Needs **Set up** (a product type's units: an administrator) | yes |

The registers are `partners`, `products`, `accounts`, `units` and `cost-centres`. Rows go through the register's own service, so the user's write right on the category is checked. A Lookup property is added on the Set up screen.

## Production, stock and the till

| Tool | Does | Changes |
|---|---|---|
| `calculate_production` | What making (or taking apart) a quantity of a product takes by its recipe, with what the place holds; needs **Production** | no |
| `produce` | A Production document by the recipe (or the ingredients given), or from what is left of a customer order; with `disassemble=true` a Disassembly. Posted at once: final, and not reversed. Needs **Production** | yes, final |
| `convert_stock` | A Unit split: stock of a product from one unit into another, at the same value. Posted at once. Needs **Stock convert** | yes, final |
| `pos_shift` | A point of sale's shift: `current`, `open` (cash in the drawer), `close` (cash counted; expected cash and difference shown then; final), `list` (needs **POS shifts**). Needs **POS** | open and close |
| `unpaid_pos_bills` | A point of sale's open bills and its settings (bill and payment types, cash desk, walk-in client, method, what is missing); needs **POS** | no |
| `pay_pos_bills` | Pays named bills, a table's or all, one money document per client, posted at once; needs **POS** | yes, final |

A counter sale is a bill of the point of sale's bill type: write it with `save_draft_document` and post it, then pay it with `pay_pos_bills`.

## Who sees what (administrators)

| Tool | Does | Changes |
|---|---|---|
| `role_access` | A role's or a user's menu (every node the company offers, with given) and rights per entity and category | no |
| `set_role_access` | Sets the menu nodes held and/or the rights (read all, read own, write, forward) of a role or a user; each list is the whole list | yes |

## Around documents

| Tool | Does | Changes |
|---|---|---|
| `switch_off_document` | Switches off a draft, or a posted offer, contract or order (it booked nothing), with the documents under it; refused for a posted document that booked something, when documents were made from it, or while another user edits it | yes |
| `hold_document` | `holder`, `hold` or `release`: the editor's hold, so no one else changes it meanwhile | the hold only |
| `email_document` | Sends the document's PDF by e-mail through the company's mail server | yes |
| `set_document_type_users` | Gives a document type to users and roles (the whole lists); administrators | yes |
| `approvals`, `approve_document` | The documents waiting for approval; approves one within the user's limit. Needs **Approvals** | `approve_document` yes |
| `contract_billing` | `due` (needs **Invoice run**), `renewals` (needs **Renewals**), a contract's `plan` | no |
| `run_contract_billing` | `run` the invoice run up to a day (posted, final), `renew` a contract to a new end date, set a contract's billing `plan` | yes |
| `year_closing` | `check` a fiscal year, or `close` it (final). Needs **Year closing** | close |
| `save_currency` | Adds, changes or switches off a currency; administrators with **Currencies** | yes |

## Assets, lots, bank statements, repairs

| Tool | Does | Changes |
|---|---|---|
| `depreciation` | Books, fixed-asset products, a book's assets, a run calculated (nothing saved), runs, a run's lines, an asset's history. Needs **Depreciation** | no |
| `run_depreciation` | `register` an asset, `run` a book on a date, make its `journal` (posted when the book posts), `undo` the latest run, `dispose` an asset value | yes |
| `lots_and_serials` | Lots (list, stock) and serial numbers (list, stock, moves). Needs **Lots** / **Serials** | no |
| `assign_lots_serials` | Gives stock held at a place a lot (number, expiry, quantity) or serial numbers | yes |
| `bank_statements` | The imported statements, a statement's lines, a line's matches and new-document types. Needs **Bank statements** | no |
| `decide_statement_line` | `match` a line to a document, `ignore` it, `clear` the decision, or `remove` a statement | yes |
| `repairs`, `repair` | The repairs board; `new`, `move`, `technician`, `offer`, `bill`, `decide`. Needs **Repairs** | `repair` yes |

Not covered: Lookup properties of a type; access tokens and passwords other than a new user's first one; files to upload (bank statement import, migration import, the Files screen); hotel stays, show seats and the production display (on the screens only).

## Related

- [Gamma ERP with Claude](README.md)
- [Prompts and the guide](prompts-and-guide.md)
- [Connect Claude](connecting.md)

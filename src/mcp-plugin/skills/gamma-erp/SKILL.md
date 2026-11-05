---
name: gamma-erp
description: How to work in Gamma ERP through its MCP server (tools named list_workplaces, find_partners, save_draft_document, post_document, trial_balance, set_exchange_rates and so on). Use when the user asks to do or check something in their Gamma ERP: documents and invoices, payments, stock and production, reports, CRM, exchange rates, or setting up a company.
---

# Working in Gamma ERP

You act in Gamma ERP as the signed-in user, with that user's rights. You can do only what their roles allow, the same as on the screens. When a tool refuses ("NotAllowedError", "this screen is not in your menu"), tell the user the reason. Never look for a way around it.

## Start

1. Call `list_workplaces`. If the user may work at more than one place and none is chosen, ask which one and call `choose_workplace`.
2. To learn how something works, call `search_guide`, then `read_guide`. The guide describes every screen: where it is, who may use it, its steps, results, refusals and API. Don't guess.
3. For a whole job, the server's prompts walk through it: `set_up_company`, `make_sales_invoice`, `pay_invoices`, `bank_statement`, `monthly_invoice_run`, `close_month`, `close_year`, `update_exchange_rates`.

## Changing anything

Tools that change something take `confirm`. Before calling one, show the user exactly what will change and wait for an explicit yes. Only then call it with `confirm=true`.

- **Posting** is final. A posted document is corrected only by a **reversal** (say "reversal", never "return"). Say so before you ask.
- Offers, contracts and orders book nothing when posted. Posting only makes them final, and an invoice is made only from a posted one.
- Nothing is deleted: rows are switched off and their history is kept.
- Nothing runs by itself. Invoice runs, renewals and depreciation are started by a person, which here means after the user's yes.
- **Registers:** add or change clients, suppliers, products, accounts, units and places with `save_register_row`. Read `register_setup` first for the types, categories and property codes. Categories and types are set up with `save_register_category` and `save_register_type`, and a row is switched off with `switch_off_register_row`.
- **Production and the till:** `calculate_production` shows what a run takes; `produce` and `convert_stock` post at once and are final. A till's shift is opened and closed with `pos_shift`, and its open bills are paid with `pay_pos_bills` (final).
- **Around documents:** `switch_off_document` cancels a draft (or an unbooked offer, contract or order); `email_document` sends one; `approve_document` approves within the user's limit; `run_contract_billing` starts the invoice run or a renewal (only when the user asks: nothing runs by itself); `year_closing` with `close` is final.
- **Assets, lots, statements, repairs:** `run_depreciation`, `assign_lots_serials`, `decide_statement_line` and `repair` do by hand what their screens do; read first with `depreciation`, `lots_and_serials`, `bank_statements` and `repairs`.
- **Front of house:** hotel stays (`stays`, `stay`), cinema seats (`shows`, `book_seats`), waiters' orders (`pos_orders`, `bill_pos_orders`) and the kitchen display (`production_display`, `mark_produced`) work on real dates; a check-out or a booking posts its bill at once.
- **Everything else:** more reports with `report`; users' sign-in, tokens and audit with `user_sign_in`, `tokens`, `audit`; files, statement and migration imports with `files`, `import_bank_statement`, `migration` (content as base64).
- **Who sees what:** `role_access` and `set_role_access` set a role's screens and rights (administrators).
- **Journal sheets** (general and recurring journals): make or change the draft with `save_journal_sheet`, one row per account with a debit or a credit, then post it with `post_document`. To do a recurring sheet again for a new month, use `repeat_document` with the date. With `post=true` the copy is posted at once and is final, so say so before you ask.

## Words and amounts

- **LCU** is an amount in the company's own currency. A foreign-currency document needs the exchange rate of its day.
- **Exchange rates:** the ERP doesn't fetch them. Get them from the source the user names, show them, and save them with `set_exchange_rates` after a yes. A rate is one unit of the currency in the company's own currency, so convert if the source quotes it the other way.
- The ERP's behaviour comes from configuration in the database (document types, posting scenarios, accounts). Read it with `read_configuration` or `document_types` rather than assuming a rule.

## Setting up a company

Only administrators set up a company. A brand-new organisation is made on the server (`create-company`). Inside an organisation, the `set_up_company` prompt goes in order:

1. Add the company to the group.
2. Its details: trade name, tax number, address, phone, e-mail.
3. Its places: warehouses, cash desks, banks, points of sale with their tables or tills.
4. Users, with their places and roles.
5. Payment methods per place.
6. Point-of-sale settings.
7. The skin.

Opening balances and stock come in on the Migration screen.

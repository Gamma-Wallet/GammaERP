# Open points

Everything the documentation writers could not confirm or found incomplete, collected from each page. These come from reading the code, not from running it. Each one is for the founder to decide; nothing here has been changed.

## [currencies-and-payments/currencies-and-rates.md](currencies-and-payments/currencies-and-rates.md)

- Rates are typed by hand; the ERP fetches none. Fetching them is planned for the Gamma ERP MCP.

## [currencies-and-payments/payment-methods-by-cost-centre.md](currencies-and-payments/payment-methods-by-cost-centre.md)

- Not tried with data yet: a card payment at a point of sale going to the linked bank needs a company made after 2026-11-04 (the point-of-sale receipt of money and refund got a bank-account line in the packs then).

## [documents/categories/cash-desk.md](documents/categories/cash-desk.md)

- Point-of-sale cash documents come from the shift screens. Whether a person writes them here depends on who is given them.

## [documents/categories/transfers.md](documents/categories/transfers.md)

- Several point-of-sale types here are named for the point of sale. Whether a person starts them here or they come from the point-of-sale screens depends on who is given them.

## [documents/drafts.md](documents/drafts.md)

- With no timeout, a hold left by a browser closed without signing out stays until that user opens and leaves the document, or signs out.

## [point-of-sale/paying.md](point-of-sale/paying.md)

- Open orders that are not billed yet are not in the unpaid list. They show on the table only.

## [products/product-types-and-categories.md](products/product-types-and-categories.md)

- Switching tracking on for a type that already holds stock leaves that stock without lots or numbers. Give them on the Lots and Serial numbers screens.

## [repairs/board.md](repairs/board.md)

- A job has no delete action on the board. It leaves when it reaches a final stage and 30 days pass.

## [repairs/new-job.md](repairs/new-job.md)

- The client search reads all clients the user can see in configuration, not only those of the repair shop.

## [system-settings/menu.md](system-settings/menu.md)

- The model `menu-access` (`tbl_inter_menu_user`) can be read and written through `/config/menu-access` by an administrator too. No menu item opens it. Rights is the screen for it.

## [system-settings/types-and-categories.md](system-settings/types-and-categories.md)

- Lookup scopes are rows of `tbl_elements`. `GET /lookups` lists them, and they have no screen of their own.

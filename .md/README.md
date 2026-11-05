# Gamma ERP documentation

Every action a person can do in Gamma ERP, by feature, in the same order as the menu. Each page says where the action is, who may do it, the steps, the result, what is refused, and the API call.

- [Architecture](architecture.md): how the system is built.
- [Open points](open-points.md): what is incomplete or still to decide, collected from every page.

## Features

- [Start here](getting-started/README.md): From a new company on the server to a person working at their cost centre.
- [Documents](documents/README.md): The Documents menu: every invoice, payment, transfer, offer, order, contract and journal in the ERP. All of them share one list screen and one editor.
- [Contracts](contracts/README.md): Billing contracts every so many months, and renewing them. A person starts each step: nothing bills or renews by itself. The contracts themselves are 
- [CRM](crm/README.md): Sales being worked on with clients and leads. A person moves each opportunity from stage to stage by hand. Nothing moves by itself.
- [Clients and suppliers](partners/README.md): Partners are the clients, suppliers and leads a company deals with. One register holds them all; the categories say which is which.
- [Repairs](repairs/README.md): A client leaves an item, gets an intake document, gets an offer of parts and repair time, accepts or refuses it, and is billed when they come back. Ev
- [Point of sale](point-of-sale/README.md): The touch screen for selling at a restaurant, shop, cinema or hotel, plus the repair board for a repair shop. The kind of a point of sale decides whic
- [Products and services](products/README.md): What the company sells, buys and keeps, the prices it sells at, and the units it counts in.
- [Stock and production](stock/README.md): Making products, converting stock, tracking batches and single units, and how stock is valued. Everything here is done by hand. Production and convers
- [Accounting setup](accounting/README.md): Everything under the **Accounting setup** menu: the accounts, how documents post to them, statement lines, closing, and VAT.
- [Fixed assets](fixed-assets/README.md): Everything under the **Fixed assets** menu. An asset is a product and unit with a value in a depreciation book. Depreciation is always started by hand
- [Currencies and payments](currencies-and-payments/README.md): Everything under the **Currencies and payments** menu.
- [Reports](reports/README.md): Everything under the **Reports** menu, and the Dashboard at the top of the menu. This page holds what every report shares. Reports read posted data; n
- [Company](company/README.md): The Company menu: the company's places, its staff, what it buys from its group, and its files.
- [Users and rights](users-and-rights/README.md): The Users and rights menu: who signs in, what they may see and do, up to what total they may approve, and what they did.
- [System settings](system-settings/README.md): The System settings menu: how data comes in, how documents are configured, and how the menu is built.
- [Gamma ERP with Claude](mcp/README.md): The ERP's MCP server lets Claude work in Gamma ERP for a user. It can:

## Rules that hold everywhere

- Nothing runs by itself: every step is started by a person.
- Behaviour is configuration in the database, per company, not fixed in code.
- Nothing is deleted: a row is switched off, and history is kept.
- A posted document is final: it is corrected by a reversal.
- Each company has its own language plus English.
- There is no public sign-up: a company is made on the server with the create-company command.

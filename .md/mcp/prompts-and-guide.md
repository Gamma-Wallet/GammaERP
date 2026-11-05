# Prompts and the guide

How Claude learns to use the ERP: the server's instructions, this user guide, named prompts for the main jobs, and a skill for Claude Code.

**Where:** in Claude. **Who:** anyone connected.

## Instructions

When Claude connects, the server sends it the ERP's rules:
- work with the user's rights, and report a refusal as the ERP's reason;
- choose the workplace first;
- nothing is deleted;
- a posted document is final and is corrected by a reversal;
- offers, contracts and orders book nothing when posted;
- amounts in the company's currency are LCU;
- nothing runs by itself;
- show every change and wait for an explicit yes;
- the ERP doesn't fetch exchange rates.

## The user guide

Every page of this guide is built into the API:
- **Resources:** `gamma-erp://guide/<path>`, one per page, for clients that browse resources.
- **Tools:** `search_guide` (words, the best pages with an excerpt) and `read_guide` (one page by its path; `README.md` is the index).

The guide is read-only and is the same for every user.

## Prompts

Named workflows. Claude Code shows them as slash commands; claude.ai and Desktop offer them in the connector's menu.

| Prompt | Does |
|---|---|
| `set_up_company` | Walks an administrator through a company's setup: group, details, places, users and roles, payment methods, point of sale, skin, then what is still missing |
| `make_sales_invoice` | Makes a sales invoice as a draft, shows it, and posts it after a yes |
| `pay_invoices` | Records a payment covering open invoices chosen by number |
| `bank_statement` | Helps match a bank statement's lines to documents |
| `monthly_invoice_run` | Lists the contracts due and makes their invoices after a yes |
| `close_month` | Checks a month (drafts, trial balance, VAT) and closes the period after a yes |
| `close_year` | Checks a year and closes it, with the currency change if any |
| `update_exchange_rates` | Takes rates from the source you name and saves them after a yes |

## The Claude Code plugin

`src/mcp-plugin` in the repository holds:
- the connection: `.mcp.json`, reading `GAMMA_ERP_URL` and `GAMMA_ERP_TOKEN` from the environment;
- the skill `gamma-erp`: how to work in the ERP, the same rules as above.

Start Claude Code with `claude --plugin-dir ./src/mcp-plugin`, or add the plugin to your team's plugin marketplace.

## Related

- [Gamma ERP with Claude](README.md)
- [Tools](tools.md)

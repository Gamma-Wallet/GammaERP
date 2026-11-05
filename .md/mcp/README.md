# Gamma ERP with Claude

The ERP's MCP server lets Claude work in Gamma ERP for a user. It can:
- find documents, partners and products;
- read reports;
- make drafts, and post or reverse them after the user's yes;
- save exchange rates;
- set up a company.

Claude always works as the signed-in user, with that user's rights. It can do no more than the user can do on the screens.

The server is part of the API, at `https://<your ERP>/mcp`. Its code is in `src/backend/Gamma.Erp.Api/Mcp/`, built on the official C# MCP SDK (Apache-2.0).

| File | What it covers |
|------|----------------|
| [connecting.md](connecting.md) | Connecting Claude Code, Desktop (with a token) and claude.ai (with the ERP sign-in), and choosing where you work |
| [claude-tokens.md](claude-tokens.md) | Personal Claude tokens: make, see, switch off; the first administrator's token |
| [tools.md](tools.md) | Every tool: what it does, which ones change data, and the rights each needs |
| [prompts-and-guide.md](prompts-and-guide.md) | The server's instructions, the user guide as resources, the named prompts, and the Claude Code plugin |

## Rules Claude follows

- **Changes need a yes.** Every tool that changes something takes `confirm`. Without `confirm=true` it changes nothing and asks Claude to show the change to the user first.
- **Final steps are flagged.** Posting and reversing are final, and are marked as such to Claude.
- **Rights are the screens' rights.** Each tool checks the same menu screen or report as its screen, and the services check the rest: administrators only, categories, document types, approval limits, places. A refusal reaches Claude in the ERP's own words.
- **Nothing runs by itself.** Claude starts something only when the user asks.
- **Exchange rates are saved, not fetched.** The ERP doesn't fetch rates. Claude takes them from wherever the user asks and saves them with `set_exchange_rates`.

## Related

- [Sign in](../getting-started/sign-in.md)
- [Create a company](../getting-started/create-a-company.md)
- [Users](../users-and-rights/users.md)

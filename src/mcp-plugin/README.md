# Gamma ERP plugin for Claude Code

This plugin connects Claude Code to your Gamma ERP. It adds:
- **The ERP's MCP server**, signed in with your personal Claude token.
- **A skill** that tells Claude how the ERP works.

Claude works as you and can do only what your roles allow. Before any change, it shows you what will change and waits for your yes.

## Before you start

1. **Make a token.** Sign in to Gamma ERP, open the account menu (top right) › **Claude tokens**, name a token and press **Make a token**. Copy it now: it is shown only once.
2. **Set two environment variables** for Claude Code:

   ```
   GAMMA_ERP_URL=https://erp.example.com        (your ERP's address, without /mcp)
   GAMMA_ERP_TOKEN=GE_...                       (the token you copied)
   ```

   Keep the token out of files and chats, like a password. When it is no longer used, switch it off in **Claude tokens**.

## Install

From a clone of the repository, start Claude Code with the plugin's folder:

```
claude --plugin-dir ./src/mcp-plugin
```

To install it for good, add it to a plugin marketplace your team uses, then install it from there with `/plugin`.

Or add only the server, without the skill:

```
claude mcp add --transport http gamma-erp "$GAMMA_ERP_URL/mcp" --header "Authorization: Bearer $GAMMA_ERP_TOKEN"
```

## claude.ai and Claude Desktop

On **claude.ai** (web and phone), add a custom connector with the address `https://<your ERP>/mcp` and no token. Claude opens the ERP's own sign-in page; sign in as yourself.

On **Claude Desktop**, use the same connector, or the token as above.

## What it can do

The full list of tools, prompts and rules is in the user guide: `.md/mcp/`.

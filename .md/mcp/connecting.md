# Connect Claude to Gamma ERP

How Claude reaches the ERP's MCP server, at `https://<your ERP>/mcp`, and signs in.

**Where:** in Claude, not in the ERP. **Who:** any user with a sign-in to the ERP.

## Actions

### Connect Claude Code or Claude Desktop with a token

Steps:
1. Make a token in the ERP: account menu › **Claude tokens**. See [Claude tokens](claude-tokens.md).
2. In Claude Code, add the server, keeping the token in an environment variable:

   ```
   claude mcp add --transport http gamma-erp "https://<your ERP>/mcp" --header "Authorization: Bearer $GAMMA_ERP_TOKEN"
   ```

   Or use the plugin in `src/mcp-plugin` of the repository. It holds the same connection plus a skill on how the ERP works; see its README.
3. In Claude Desktop, add a custom connector with the same address and header.

Result: Claude works as you. Its first call, `list_workplaces`, shows who you are and where you may work.

### Connect claude.ai (web and phone) with your ERP sign-in

Steps:
1. In claude.ai, add a custom connector with the address `https://<your ERP>/mcp`, with no token.
2. Claude opens the ERP's own sign-in page. Sign in as yourself. If your password was given to you, you choose your own first.

Result: Claude gets a sign-in for you (code with PKCE, renewed with a refresh token) and works with your rights.

Behind it, all standard:

| Step | Address |
|---|---|
| Without a sign-in, `/mcp` answers 401 and points to its description | `GET /.well-known/oauth-protected-resource/mcp` (RFC 9728) |
| The sign-in server's description, with its registration address | `GET /.well-known/oauth-authorization-server` (also `/.well-known/openid-configuration`) |
| Claude registers itself, once: a public client with PKCE | `POST /connect/register` (RFC 7591) |
| Sign-in and token, as for the screens | `/connect/authorize`, `/connect/token` |

Refused when:
- `invalid_redirect_uri`: registration accepts only Claude's own return addresses (`https://claude.ai/api/mcp/auth_callback`, `https://claude.com/api/mcp/auth_callback`) and this computer's (`http://localhost:…`, `http://127.0.0.1:…`).
- `invalid_client_metadata`: only public clients (`token_endpoint_auth_method` none).
- `invalid_target`: the resource Claude names isn't one of the server's MCP addresses (`Mcp:Resources`).

### Choose where you work

Steps: Claude calls `list_workplaces`. With more than one place and none chosen, it asks you and calls `choose_workplace`.

Result: every later tool works at that place, as on the screens after choosing a cost centre. The choice lasts until the server restarts. A script can send the `X-Gamma-Node` header instead.

## Set up the server (operators)

The MCP server is part of the API, so there is nothing else to run. In the Docker set-up, set `MCP_RESOURCE` to the public address of `/mcp`, for example `https://erp.example.com/mcp` (`Mcp__Resources__0`). The guide pages are built into the API image.

## Related

- [Gamma ERP with Claude](README.md)
- [Claude tokens](claude-tokens.md)
- [Tools](tools.md)

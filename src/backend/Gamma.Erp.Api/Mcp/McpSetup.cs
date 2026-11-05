using System.Text.Json.Serialization;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Gamma.Erp.Api.Mcp;

/// <summary>A client registering itself (RFC 7591), as claude.ai and Claude Code do before signing in.</summary>
public sealed record ClientRegistration(
    [property: JsonPropertyName("redirect_uris")] IReadOnlyList<string>? RedirectUris,
    [property: JsonPropertyName("client_name")] string? ClientName,
    [property: JsonPropertyName("token_endpoint_auth_method")] string? TokenEndpointAuthMethod);

/// <summary>
/// The Gamma ERP MCP server (the founder, 2026-11-04), served by the API at /mcp: Claude works as the signed-in user, with that user's rights,
/// signed in with a personal token (Bearer GE_…) or through the ERP's own sign-in page (OAuth with code and PKCE, the client registering itself).
/// Requests are stateless; where the user works is kept by <see cref="McpWorkplaces"/>.
/// </summary>
public static class McpSetup
{
    public const string Path = "/mcp";

    /// <summary>The redirect addresses a self-registered client may use: Claude's own and the user's own computer (Claude Code, Desktop).</summary>
    private static readonly string[] GrantTypes = ["authorization_code", "refresh_token"];
    private static readonly string[] ResponseTypes = ["code"];

    private static readonly string[] Allowed = ["https://claude.ai/api/mcp/auth_callback", "https://claude.com/api/mcp/auth_callback"];

    /// <summary>The MCP endpoint's public addresses, from configuration (Mcp:Resources); the local one when none is set.</summary>
    public static IReadOnlyList<string> Resources(IConfiguration configuration) =>
        configuration.GetSection("Mcp:Resources").Get<string[]>() is { Length: > 0 } set ? set : ["http://localhost:5080/mcp"];

    public static void AddGammaMcp(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpWorkplaces>();
        builder.Services.AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "gamma-erp", Title = "Gamma ERP", Version = "1.0.0" };
                options.ServerInstructions = McpGuide.Instructions;
            })
            .WithHttpTransport(options => options.Stateless = true)
            .WithToolsFromAssembly(typeof(McpSetup).Assembly)
            .WithPromptsFromAssembly(typeof(McpSetup).Assembly)
            .WithResources(GuideTools.Resources());
    }

    /// <summary>The MCP endpoint, the resource's metadata (RFC 9728, served by the MCP sign-in scheme) and the client registration endpoint.</summary>
    public static void MapGammaMcp(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapMcp(Path).RequireAuthorization();
        var resources = Resources(app.Configuration);
        app.MapPost("/connect/register", async (ClientRegistration registration, IOpenIddictApplicationManager manager, CancellationToken ct) =>
        {
            var uris = registration.RedirectUris ?? [];
            if (uris.Count == 0 || uris.Any(u => !Permitted(u)))
            {
                return Results.Json(new { error = "invalid_redirect_uri", error_description = "Only Claude's own redirect addresses and this computer's (localhost) are accepted." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (registration.TokenEndpointAuthMethod is { } method && method != "none")
            {
                return Results.Json(new { error = "invalid_client_metadata", error_description = "Only public clients (token_endpoint_auth_method none) with PKCE." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var clientId = $"mcp-{Guid.CreateVersion7():N}";
            var name = string.IsNullOrWhiteSpace(registration.ClientName) ? "Claude" : registration.ClientName.Trim()[..Math.Min(60, registration.ClientName.Trim().Length)];
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId, ClientType = ClientTypes.Public, DisplayName = name,
                Permissions =
                {
                    Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code, Permissions.Scopes.Profile, Permissions.Prefixes.Scope + Scopes.OfflineAccess,
                },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange },
            };
            foreach (var uri in uris)
            {
                descriptor.RedirectUris.Add(new Uri(uri));
            }

            foreach (var resource in resources)
            {
                descriptor.Permissions.Add(Permissions.Prefixes.Resource + resource);
            }

            await manager.CreateAsync(descriptor, ct).ConfigureAwait(false);
            return Results.Json(new
            {
                client_id = clientId, client_id_issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), client_name = name, redirect_uris = uris,
                grant_types = GrantTypes, response_types = ResponseTypes, token_endpoint_auth_method = "none",
            }, statusCode: StatusCodes.Status201Created);
        }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting(Auth.AuthEndpoints.SignInLimit).ExcludeFromDescription();
    }

    /// <summary>The MCP sign-in scheme's metadata for this host: the resource is /mcp, the authorization server is the API itself.</summary>
    public static void Describe(McpAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ResourceMetadata = new ProtectedResourceMetadata { ResourceName = "Gamma ERP" };
        options.Events.OnResourceMetadataRequest = context =>
        {
            var host = $"{context.HttpContext.Request.Scheme}://{context.HttpContext.Request.Host}";
            context.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = $"{host}{Path}", AuthorizationServers = [$"{host}/"], ScopesSupported = [Scopes.OpenId, Scopes.Profile, Scopes.OfflineAccess],
                BearerMethodsSupported = ["header"], ResourceName = "Gamma ERP",
            };
            return Task.CompletedTask;
        };
    }

    private static bool Permitted(string uri) =>
        Allowed.Contains(uri, StringComparer.Ordinal)
        || Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttp && (parsed.Host == "localhost" || parsed.Host == "127.0.0.1")
            && string.IsNullOrEmpty(parsed.Fragment);
}

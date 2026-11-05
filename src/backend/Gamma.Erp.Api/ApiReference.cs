using Scalar.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Gamma.Erp.Api;

/// <summary>
/// The interactive API reference (Scalar) at /scalar, in development only: every endpoint with its request and answer, and a sign-in with
/// the screens' client (authorization code + PKCE) so the endpoints can be tried as a real user.
/// </summary>
public static class ApiReference
{
    /// <summary>The OpenAPI security scheme of the sign-in.</summary>
    public const string Scheme = "OpenIdConnect";

    public static void MapApiReference(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var redirect = app.Configuration["Auth:ApiReferenceRedirectUri"];
        app.MapScalarApiReference((options, http) =>
        {
            options.WithTitle("Gamma ERP API").AddPreferredSecuritySchemes([Scheme]).AddAuthorizationCodeFlow(Scheme, flow => flow
                .WithClientId("gamma-erp-web").WithPkce(Pkce.Sha256).WithSelectedScopes([Scopes.OpenId, Scopes.OfflineAccess])
                .WithAuthorizationUrl($"{http.Request.Scheme}://{http.Request.Host}/connect/authorize").WithTokenUrl($"{http.Request.Scheme}://{http.Request.Host}/connect/token")
                .WithRedirectUri(redirect ?? $"{http.Request.Scheme}://{http.Request.Host}/scalar/"));
        }).AllowAnonymous();
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gamma.Erp.Api.Setup;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gamma.Erp.Api.Tests;

/// <summary>A company for a test, made as the server command makes one (nothing outside the server can), and signing in to it.</summary>
internal static class TestCompanies
{
    public static async Task<SignUpResult> CreateCompanyAsync(this IServiceProvider services, SignUpRequest request, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SetupService>().SignUpAsync(request, ct);
    }

    /// <summary>A client signed in as the screens sign in; null when the name or the password is refused.</summary>
    public static async Task<HttpClient?> SignInAsync(this WebApplicationFactory<Program> api, string username, string password, string redirect = "http://localhost/signed-in")
    {
        var browser = api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var authorize = "/connect/authorize?client_id=gamma-erp-web&response_type=code&scope=openid%20offline_access"
            + $"&redirect_uri={Uri.EscapeDataString(redirect)}&code_challenge={challenge}&code_challenge_method=S256";
        await browser.GetAsync(authorize);
        var login = await browser.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username, ["password"] = password, ["returnUrl"] = authorize,
        }));
        if (login.Headers.Location?.ToString().Contains("error=", StringComparison.Ordinal) != false)
        {
            return null;
        }

        var code = System.Web.HttpUtility.ParseQueryString((await browser.GetAsync(authorize)).Headers.Location!.Query)["code"];
        var token = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "gamma-erp-web", ["code"] = code!, ["redirect_uri"] = redirect, ["code_verifier"] = verifier,
        }));
        token.EnsureSuccessStatusCode();
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            JsonDocument.Parse(await token.Content.ReadAsStringAsync()).RootElement.GetProperty("access_token").GetString());
        return client;
    }
}

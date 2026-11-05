using System.Net;
using System.Security.Claims;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Persistence;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Gamma.Erp.Api.Auth;

/// <summary>The signed-in user, their roles and the cost centres they may work at.</summary>
public sealed record MeDto(Guid Id, string? Username, string? Name, bool IsAdministrator, IReadOnlyList<MeRoleDto> Roles, IReadOnlyList<MeCostCenterDto> CostCenters, string? Language = null);

public sealed record MeRoleDto(Guid Id, string? Code, string? Name);

/// <summary>A cost centre the user may work at, with the company it is in (null above the companies).</summary>
/// <summary>
/// A cost centre the user may work at: linked to them, or a workplace below a linked one (ParentId places it in the entry page's tree).
/// </summary>
public sealed record MeCostCenterDto(Guid Id, string? Code, string? Name, Guid? CompanyId, string? CompanyName, Guid? ParentId = null, bool Linked = true, string Language = TranslatedText.DefaultLanguage, bool IsGroup = false);

/// <summary>
/// Sign-in with OpenID Connect (OpenIddict): the screens use the authorization code flow with PKCE; this host shows the sign-in page,
/// checks the password (<see cref="SignInService"/>) and issues the tokens. The token endpoint (code and refresh token exchange) is
/// OpenIddict's own.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>The claim carrying the user's corporate (tenant).</summary>
    public const string TenantClaim = "tenant";

    /// <summary>The Gamma logo as a data link: the sign-in page is one piece of HTML, and the API serves no files.</summary>
    private static readonly Lazy<string> Logo = new(() =>
    {
        using var stream = typeof(AuthEndpoints).Assembly.GetManifestResourceStream("gamma-logo-white.png")!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return "data:image/png;base64," + Convert.ToBase64String(bytes.ToArray());
    });

    public const string LoginScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    /// <summary>The limit on tries from one network address, per minute: signing in, asking for a password code, using one.</summary>
    public const string SignInLimit = "sign-in";

    /// <summary>What someone over the limit reads.</summary>
    public static string TooManyTries => Notice("Too many tries", "Wait a minute and try again.", null);

    public static void MapAuth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/account/login", (PasswordResetService reset, string? returnUrl, string? error, string? username) =>
            Results.Content(Page(returnUrl, error, username, reset.Enabled, HintOf(returnUrl, "ui_locales")), "text/html")).AllowAnonymous().ExcludeFromDescription();
        app.MapPost("/account/login", async (HttpContext http, SignInService signIn, CancellationToken ct) =>
        {
            var form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
            var returnUrl = form["returnUrl"].ToString();
            var result = await signIn.CheckAsync(form["username"].ToString(), form["password"].ToString(), ct).ConfigureAwait(false);
            if (result.User is not { } user)
            {
                return Results.Redirect($"/account/login?returnUrl={WebUtility.UrlEncode(returnUrl)}&error={WebUtility.UrlEncode(result.Error)}");
            }

            // A password given by someone else is changed first (the founder, 2026-11-04): nothing is signed in until it is.
            if (user.MustChangePassword)
            {
                return Results.Content(FirstPasswordPage(user.Username, returnUrl, null, HintOf(returnUrl, "ui_locales")), "text/html");
            }

            return await SignInAsync(http, user, returnUrl).ConfigureAwait(false);
        }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting(SignInLimit).ExcludeFromDescription();
        app.MapPost("/account/first-password", async (HttpContext http, SignInService signIn, Gamma.Erp.Persistence.ErpDbContext db, CancellationToken ct) =>
        {
            var form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
            var (username, returnUrl, password) = (form["username"].ToString(), form["returnUrl"].ToString(), form["password"].ToString());
            var language = HintOf(returnUrl, "ui_locales");
            var result = await signIn.CheckAsync(username, form["current"].ToString(), ct).ConfigureAwait(false);
            if (result.User is not { } user)
            {
                return Results.Redirect($"/account/login?returnUrl={WebUtility.UrlEncode(returnUrl)}&error={WebUtility.UrlEncode(result.Error)}");
            }

            if (password != form["confirm"].ToString())
            {
                return Results.Content(FirstPasswordPage(username, returnUrl, "The two passwords are different.", language), "text/html");
            }

            if (SignInService.Matches(user.PasswordHash!, password))
            {
                return Results.Content(FirstPasswordPage(username, returnUrl, "Choose a password different from the one you were given.", language), "text/html");
            }

            try
            {
                await AccessService.StoreAsync(db, user.Id, password, ct).ConfigureAwait(false);
            }
            catch (Gamma.Erp.Services.ValidationException refused)
            {
                return Results.Content(FirstPasswordPage(username, returnUrl, string.Join(' ', refused.Errors.Values), language), "text/html");
            }

            return await SignInAsync(http, user, returnUrl).ConfigureAwait(false);
        }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting(SignInLimit).ExcludeFromDescription();

        // The forgotten password: the same answer for everyone, whether the user exists or not.
        app.MapGet("/account/forgot", (PasswordResetService reset, string? language) => reset.Enabled ? Results.Content(ForgotPage(language), "text/html") : Results.Redirect("/account/login"))
            .AllowAnonymous().ExcludeFromDescription();
        app.MapPost("/account/forgot", async (HttpContext http, PasswordResetService reset, CancellationToken ct) =>
        {
            var form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
            var username = form["username"].ToString();
            await reset.RequestAsync(username, ct).ConfigureAwait(false);
            return Results.Content(CodePage(username, null, form["language"]), "text/html");
        }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting(SignInLimit).ExcludeFromDescription();
        app.MapGet("/account/reset", (PasswordResetService reset, string? username, string? language) =>
            reset.Enabled ? Results.Content(CodePage(username, null, language), "text/html") : Results.Redirect("/account/login")).AllowAnonymous().ExcludeFromDescription();
        app.MapPost("/account/reset", async (HttpContext http, PasswordResetService reset, CancellationToken ct) =>
        {
            var form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
            var (username, code, password, language) = (form["username"].ToString(), form["code"].ToString(), form["password"].ToString(), form["language"].ToString());
            if (password != form["confirm"].ToString())
            {
                return Results.Content(CodePage(username, "The two passwords are different.", language), "text/html");
            }

            try
            {
                return Results.Content(await reset.ResetAsync(username, code, password, ct).ConfigureAwait(false)
                    ? Notice("Password changed", "Sign in with your new password.", language)
                    : CodePage(username, "The code is wrong or no longer works.", language), "text/html");
            }
            catch (ValidationException refused)
            {
                return Results.Content(CodePage(username, string.Join(' ', refused.Errors.Values), language), "text/html");
            }
        }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting(SignInLimit).ExcludeFromDescription();

        app.MapMethods("/connect/authorize", ["GET", "POST"], async (HttpContext http, ErpDbContext db, CancellationToken ct) =>
        {
            var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenID Connect request.");
            var login = await http.AuthenticateAsync(LoginScheme).ConfigureAwait(false);
            if (!login.Succeeded)
            {
                return Results.Challenge(new AuthenticationProperties { RedirectUri = http.Request.PathBase + http.Request.Path + QueryString.Create(
                    http.Request.HasFormContentType ? [.. http.Request.Form] : [.. http.Request.Query]) }, [LoginScheme]);
            }

            var userId = Guid.Parse(login.Principal!.FindFirstValue(Claims.Subject)!);
            var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
            if (user is null)
            {
                await http.SignOutAsync(LoginScheme).ConfigureAwait(false);
                return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, user.Id.ToString()).SetClaim(TenantClaim, user.TenantId.ToString()).SetClaim(Claims.Name, user.Username);
            identity.SetScopes(request.GetScopes());
            identity.SetDestinations(_ => [Destinations.AccessToken, Destinations.IdentityToken]);
            return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }).AllowAnonymous().DisableAntiforgery().ExcludeFromDescription();

        // Who is signed in and where they may work: the screens read it before a cost centre is chosen (every /api call needs one).
        app.MapGet("/account/me", async (HttpContext http, ErpDbContext db, TenantContext tenant, CancellationToken ct) =>
        {
            if (!Guid.TryParse(http.User.FindFirstValue(Claims.Subject), out var userId) || !Guid.TryParse(http.User.FindFirstValue(TenantClaim), out var tenantId))
            {
                return Results.Problem("The token does not name a user and a corporate.", statusCode: StatusCodes.Status401Unauthorized);
            }

            tenant.Set(tenantId, userId);
            var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
            if (user is null)
            {
                return Results.Problem("The signed-in user is not there.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var roles = await (from l in db.Set<InterUserCategory>().AsNoTracking() join c in db.Set<UserCategory>().AsNoTracking() on l.CategoryId equals c.Id
                               where l.ElementId == userId select c).ToListAsync(ct).ConfigureAwait(false);
            var nodeIds = await db.Set<InterUserCostCenter>().AsNoTracking().Where(l => l.UserId == userId && l.CostCenterId != null).Select(l => l.CostCenterId!.Value)
                .ToListAsync(ct).ConfigureAwait(false);
            var nodes = await Workplaces.OfferedAsync(db, nodeIds, ct).ConfigureAwait(false);
            var roleIds = roles.Select(r => r.Id).ToHashSet();
            var administrator = roles.Any(r => r.IsAdministrator);
            var places = new List<MeCostCenterDto>();
            // A group keeps its own books, so it counts as a company too: its type is what says it is the group.
            var groupTypes = await db.Set<CostCenterType>().AsNoTracking().Where(t => t.Code == "CORPORATE").Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false);
            var offered = nodes.Select(n => n.Place.Id).ToHashSet();
            foreach (var (node, linked) in nodes)
            {
                var visibility = new Visibility(db, new RequestContext(tenantId, userId, node.Id, roleIds, administrator));
                var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
                var companyName = company is { } c ? (await db.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == c, ct).ConfigureAwait(false))?.Name.In("en") : null;
                places.Add(new MeCostCenterDto(node.Id, node.Code, node.Name.In("en"), company, companyName,
                    node.IdSup is { } up && offered.Contains(up) ? up : null, linked, await visibility.CompanyLanguageAsync(ct).ConfigureAwait(false), groupTypes.Contains(node.TypeId)));
            }

            return Results.Ok(new MeDto(user.Id, user.Username, user.Name.In("en"), administrator,
                [.. roles.Select(r => new MeRoleDto(r.Id, r.Code, r.Name?.In("en")))], places, user.Language));
        }).RequireAuthorization().ExcludeFromDescription();

        app.MapMethods("/connect/logout", ["GET", "POST"], async (HttpContext http, ErpDbContext db, SignInService signIn, CancellationToken ct) =>
        {
            var login = await http.AuthenticateAsync(LoginScheme).ConfigureAwait(false);
            if (login.Succeeded && Guid.TryParse(login.Principal!.FindFirstValue(Claims.Subject), out var userId)
                && await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false) is { } user)
            {
                await signIn.RecordAsync(user, signedIn: false, ct).ConfigureAwait(false);
            }

            await http.SignOutAsync(LoginScheme).ConfigureAwait(false);
            return Results.SignOut(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }).AllowAnonymous().DisableAntiforgery().ExcludeFromDescription();
    }

    // The sign-in page in the screens' theme (Gamma ERP's own: light grey canvas, dark top bar, white card, serif title, black button).
    /// <summary>The user name the page starts with: the one given, else the sign-in request's login_hint.</summary>
    internal static string? UsernameOf(string? returnUrl, string? username) => string.IsNullOrWhiteSpace(username) ? HintOf(returnUrl, "login_hint") : username.Trim();

    // A value the sign-in request carries: login_hint, or ui_locales, the language the browser remembers.
    private static string? HintOf(string? returnUrl, string name)
    {
        var query = returnUrl is { } url && url.IndexOf('?', StringComparison.Ordinal) is var i and >= 0 ? url[(i + 1)..] : null;
        var hint = query is null ? null : System.Web.HttpUtility.ParseQueryString(query)[name];
        return string.IsNullOrWhiteSpace(hint) ? null : hint.Trim();
    }

    private static string Page(string? returnUrl, string? error, string? username, bool forgotten, string? language) => Shell("sign in", $$"""
        <form method="post" action="/account/login">
        <h1>{{Word("Sign in", language)}}</h1><p class="sub">{{Word("Use your Gamma ERP user name and password.", language)}}</p>
        {{ErrorLine(error, language)}}
        <label>{{Word("User name", language)}}<input name="username" autocomplete="username" required {{(UsernameOf(returnUrl, username) is { } name ? $"value=\"{WebUtility.HtmlEncode(name)}\"" : "autofocus")}}></label>
        <label>{{Word("Password", language)}}<input name="password" type="password" autocomplete="current-password" required {{(UsernameOf(returnUrl, username) is null ? string.Empty : "autofocus")}}></label>
        <input type="hidden" name="returnUrl" value="{{WebUtility.HtmlEncode(returnUrl ?? string.Empty)}}"><button type="submit">{{Word("Sign in", language)}}</button>
        {{(forgotten ? $"<p class=\"new\"><a href=\"/account/forgot{InLanguage(language)}\">{Word("Forgot your password?", language)}</a></p>" : string.Empty)}}
        </form>
        """, language);

    /// <summary>Signs the user in on this host and goes back into the sign-in flow it came from.</summary>
    private static async Task<IResult> SignInAsync(HttpContext http, Gamma.Erp.Models.Users.User user, string returnUrl)
    {
        var identity = new ClaimsIdentity([new Claim(Claims.Subject, user.Id.ToString()), new Claim(TenantClaim, user.TenantId.ToString()),
            new Claim(Claims.Name, user.Username ?? string.Empty)], LoginScheme);
        await http.SignInAsync(LoginScheme, new ClaimsPrincipal(identity)).ConfigureAwait(false);
        // Only back into this host's own flow (no open redirect).
        return Results.Redirect(returnUrl.StartsWith("/connect/authorize", StringComparison.Ordinal) && !returnUrl.Any(char.IsControl) ? returnUrl : "/");
    }

    private static string FirstPasswordPage(string? username, string? returnUrl, string? error, string? language) => Shell("new password", $$"""
        <form method="post" action="/account/first-password">
        <h1>{{Word("Choose your own password", language)}}</h1><p class="sub">{{Word("Your password was given to you. Choose your own before you go on; your Claude tokens keep working.", language)}}</p>
        {{ErrorLine(error, language)}}
        <input type="hidden" name="username" value="{{WebUtility.HtmlEncode(username ?? string.Empty)}}"><input type="hidden" name="returnUrl" value="{{WebUtility.HtmlEncode(returnUrl ?? string.Empty)}}">
        <label>{{Word("The password you were given", language)}}<input name="current" type="password" autocomplete="current-password" required autofocus></label>
        <label>{{Word($"New password (at least {Setup.SetupService.MinPasswordLength} characters)", language)}}<input name="password" type="password" autocomplete="new-password" required></label>
        <label>{{Word("The same again", language)}}<input name="confirm" type="password" autocomplete="new-password" required></label>
        <button type="submit">{{Word("Change the password", language)}}</button>
        </form>
        """, language);

    private static string ForgotPage(string? language) => Shell("forgotten password", $$"""
        <form method="post" action="/account/forgot">
        <h1>{{Word("Forgotten password", language)}}</h1><p class="sub">{{Word("Enter your user name: a code to choose a new password goes to your e-mail address.", language)}}</p>
        <label>{{Word("User name", language)}}<input name="username" autocomplete="username" required autofocus></label>
        <input type="hidden" name="language" value="{{WebUtility.HtmlEncode(language ?? string.Empty)}}"><button type="submit">{{Word("Send the code", language)}}</button>
        <p class="new"><a href="/account/reset{{InLanguage(language)}}">{{Word("I already have a code", language)}}</a></p>
        <p class="new"><a href="/account/login">{{Word("Back to sign in", language)}}</a></p>
        </form>
        """, language);

    private static string CodePage(string? username, string? error, string? language) => Shell("new password", $$"""
        <form method="post" action="/account/reset">
        <h1>{{Word("New password", language)}}</h1><p class="sub">{{Word($"If this user has an e-mail address, a code is on its way. It works for {PasswordResetService.Valid.TotalMinutes:0} minutes. No e-mail? Ask your administrator.", language)}}</p>
        {{ErrorLine(error, language)}}
        <label>{{Word("User name", language)}}<input name="username" autocomplete="username" required value="{{WebUtility.HtmlEncode(username ?? string.Empty)}}"></label>
        <label>{{Word("Code", language)}}<input name="code" autocomplete="one-time-code" autocapitalize="characters" spellcheck="false" maxlength="6" required autofocus></label>
        <label>{{Word($"New password (at least {Setup.SetupService.MinPasswordLength} characters)", language)}}<input name="password" type="password" autocomplete="new-password" required></label>
        <label>{{Word("The same again", language)}}<input name="confirm" type="password" autocomplete="new-password" required></label>
        <input type="hidden" name="language" value="{{WebUtility.HtmlEncode(language ?? string.Empty)}}"><button type="submit">{{Word("Change the password", language)}}</button>
        <p class="new"><a href="/account/login">{{Word("Back to sign in", language)}}</a></p>
        </form>
        """, language);

    private static string Notice(string title, string text, string? language) => Shell(title.ToLowerInvariant(),
        $"<section><h1>{Word(title, language)}</h1><p class=\"sub\">{Word(text, language)}</p><p class=\"new\"><a href=\"/\">{Word("Sign in", language)}</a></p></section>", language);

    private static string Word(string text, string? language) => WebUtility.HtmlEncode(Messages.Translate(text, language));

    private static string InLanguage(string? language) => string.IsNullOrEmpty(language) ? string.Empty : $"?language={WebUtility.UrlEncode(language)}";

    private static string ErrorLine(string? error, string? language) => string.IsNullOrEmpty(error) ? string.Empty : $"<p class=\"error\" role=\"alert\">{ErrorIcon}<span>{Word(error, language)}</span></p>";

    // The app's "critical" icon (lucide octagon-alert, ISC), so the error carries an icon and a word like every state in the app.
    private const string ErrorIcon = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\"><path d=\"M12 16h.01\"/><path d=\"M12 8v4\"/><path d=\"M15.312 2a2 2 0 0 1 1.414.586l4.688 4.688A2 2 0 0 1 22 8.688v6.624a2 2 0 0 1-.586 1.414l-4.688 4.688a2 2 0 0 1-1.414.586H8.688a2 2 0 0 1-1.414-.586l-4.688-4.688A2 2 0 0 1 2 15.312V8.688a2 2 0 0 1 .586-1.414l4.688-4.688A2 2 0 0 1 8.688 2z\"/></svg>";

    private static string Shell(string title, string body, string? language) => $$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Gamma ERP: {{title}}</title>
        <link rel="icon" href="/favicon.ico"><link rel="apple-touch-icon" href="/apple-touch-icon.png">
        <style>
        @font-face{font-family:Inter;font-style:normal;font-weight:400;font-display:swap;src:url(/fonts/inter-latin-400-normal.woff2) format("woff2")}
        @font-face{font-family:Inter;font-style:normal;font-weight:500;font-display:swap;src:url(/fonts/inter-latin-500-normal.woff2) format("woff2")}
        @font-face{font-family:Newsreader;font-style:normal;font-weight:500;font-display:swap;src:url(/fonts/newsreader-latin-500-normal.woff2) format("woff2")}
        :root{color-scheme:light;--canvas:#f1f3f6;--surface:#ffffff;--ink:#121a24;--muted:#5b6878;--line:#e0e5eb;--line-strong:#8994a3;--topbar:#11233a;--topbar-ink:#f2f6fa;--link:#1a5fb4;--focus:#2585bd;--primary:#163252;--primary-hover:#21446d;--primary-ink:#ffffff;--critical:#b42318;--critical-bg:#fdeceb;--font-title:"Newsreader","Source Serif 4",Georgia,serif;--font-text:Inter,system-ui,-apple-system,"Segoe UI",sans-serif;--radius:8px;--radius-l:12px;--control-h:36px}
        *{box-sizing:border-box}body{margin:0;min-height:100vh;display:flex;flex-direction:column;background:var(--canvas);color:var(--ink);font:14px/1.45 var(--font-text);-webkit-font-smoothing:antialiased}
        header{height:52px;background:var(--topbar);color:var(--topbar-ink);display:flex;align-items:center;gap:10px;padding:0 20px;font-weight:600;letter-spacing:.01em}
        header img{display:block;height:26px;width:auto}
        main{flex:1;display:grid;place-items:center;padding:12vh 16px 32px}
        form,section{width:min(380px,100%);background:var(--surface);border:1px solid var(--line);border-radius:var(--radius-l);padding:32px;display:grid;gap:16px}
        h1{margin:0;font:500 31px/1.2 var(--font-title)}
        .sub{margin:-8px 0 4px;color:var(--muted)}
        label{display:grid;gap:6px;font-weight:500}
        input{font:inherit;font-weight:400;height:var(--control-h);padding:0 12px;border:1px solid var(--line-strong);border-radius:var(--radius);background:var(--surface);color:var(--ink)}
        input:hover{border-color:var(--muted)}input:focus-visible{outline:2px solid var(--focus);outline-offset:0;border-color:var(--focus)}
        button{font:inherit;font-weight:500;height:var(--control-h);border:1px solid var(--primary);border-radius:var(--radius);background:var(--primary);color:var(--primary-ink);cursor:pointer}
        button:hover{background:var(--primary-hover)}button:focus-visible{outline:2px solid var(--focus);outline-offset:2px}
        .error{margin:0;padding:10px 12px;border-radius:var(--radius);background:var(--critical-bg);color:var(--critical);display:flex;align-items:flex-start;gap:8px}
        .error svg{flex:none;margin-top:2px}
        .new{margin:0;color:var(--muted);text-align:center}.new a{color:var(--link);text-decoration:none}.new a:hover{text-decoration:underline}
        footer{padding:12px 16px 20px;color:var(--muted);font-size:12px;text-align:center}footer a{color:inherit}
        </style></head><body>
        <header><img src="{{Logo.Value}}" alt="Gamma"></header>
        <main>{{body}}</main>
        <footer>{{Word("Powered by", language)}} <a href="https://www.gamma-wallet.com/en" target="_blank" rel="noopener">Gamma Wallet</a></footer></body></html>
        """;
}

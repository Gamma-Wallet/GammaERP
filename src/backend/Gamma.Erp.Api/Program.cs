using Gamma.Erp.Api;
using Gamma.Erp.Api.Auth;
using Gamma.Erp.Api.Mcp;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Depreciation;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Partners;
using Gamma.Erp.Services.Posting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Erp")
    ?? throw new InvalidOperationException("Set ConnectionStrings:Erp (environment variable ConnectionStrings__Erp or user secrets); it is never kept in the repository.");

builder.Services.AddErpPersistence(connection);
builder.Services.AddDbContext<AuthDbContext>(options => options
    .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", AuthDbContext.Schema))
    .UseOpenIddict());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Items[typeof(RequestContext)] as RequestContext
    ?? throw new InvalidOperationException("No request context: the route is not under /api or the user is not signed in."));
builder.Services.AddScoped<Visibility>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<Gamma.Erp.Services.Configuration.StatutoryChartService>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped(sp => new PostingService(sp.GetRequiredService<ErpDbContext>()));
builder.Services.AddScoped<DepreciationService>();
builder.Services.AddScoped<Gamma.Erp.Services.Stock.SerialService>();
builder.Services.AddScoped<Gamma.Erp.Services.Audit.AuditService>();
builder.Services.AddScoped<Gamma.Erp.Services.Documents.MenuRightsService>();
builder.Services.AddScoped<Gamma.Erp.Services.Reports.DashboardService>();
builder.Services.AddScoped<OpenItems>();
builder.Services.AddScoped<Approvals>();
builder.Services.AddScoped<BankStatements>();
builder.Services.AddScoped<RepairService>();
builder.Services.AddScoped<Gamma.Erp.Services.Pos.ProductionDisplay>();
builder.Services.AddScoped<Gamma.Erp.Services.Pos.PosOrders>();
builder.Services.AddScoped<Gamma.Erp.Services.Pos.PosShifts>();
builder.Services.AddScoped<Gamma.Erp.Services.Stock.LotService>();
builder.Services.AddScoped<Gamma.Erp.Services.Opportunities.OpportunityService>();
builder.Services.AddScoped<Gamma.Erp.Services.Platform.SkinService>();
builder.Services.AddScoped<Gamma.Erp.Api.Auth.TokenService>();
builder.Services.AddScoped<ContractBillingService>();
builder.Services.AddScoped<Gamma.Erp.Services.Reports.YearClosing>();
builder.Services.AddScoped(Gamma.Erp.Api.MigrationSetup.Entities);
builder.Services.AddScoped<Gamma.Erp.Services.Migration.MigrationService>();
builder.Services.AddScoped<PartnerStatisticsService>();
builder.Services.AddScoped<Gamma.Erp.Services.Reports.ReportService>();
builder.Services.AddScoped<Gamma.Erp.Services.Reports.StockReportService>();
builder.Services.AddScoped<Gamma.Erp.Services.Currencies.CurrencyService>();
builder.Services.AddScoped<Gamma.Erp.Services.Stock.ProductUnitService>();
builder.Services.AddScoped<Gamma.Erp.Services.Configuration.ConfigurationService>();
builder.Services.AddScoped<Gamma.Erp.Api.Auth.AccessService>();
builder.Services.AddScoped<Gamma.Erp.Services.Pos.ReservationService>();
builder.Services.AddScoped(sp => new Gamma.Erp.Services.Pos.PosService(sp.GetRequiredService<ErpDbContext>(), sp.GetRequiredService<Visibility>(),
    sp.GetRequiredService<DocumentService>(), TimeProvider.System));
builder.Services.AddScoped<Gamma.Erp.Services.Printing.PrintService>();
builder.Services.AddScoped(sp => new Gamma.Erp.Services.Stock.UnitConversionService(sp.GetRequiredService<ErpDbContext>(), sp.GetRequiredService<RequestContext>(),
    sp.GetRequiredService<Visibility>(), TimeProvider.System));
builder.Services.AddScoped(sp => new Gamma.Erp.Services.Stock.ProductionService(sp.GetRequiredService<ErpDbContext>(), sp.GetRequiredService<RequestContext>(),
    sp.GetRequiredService<Visibility>(), TimeProvider.System));

// E-mail: the host's mail server (Mail:Host, Mail:Port, Mail:Security, Mail:User, Mail:Password, Mail:From); none set, e-mail is off.
var mail = builder.Configuration.GetSection("Mail");
var mailOptions = new Gamma.Erp.Services.Mail.MailOptions(mail["Host"], mail.GetValue("Port", 587), mail["Security"] ?? "StartTls", mail["User"], mail["Password"], mail["From"]);
builder.Services.AddSingleton(mailOptions);
builder.Services.AddSingleton<Gamma.Erp.Services.Mail.IMailSender>(new Gamma.Erp.Services.Mail.SmtpMailSender(mailOptions));
builder.Services.AddScoped<Gamma.Erp.Services.Mail.MailService>();
builder.Services.AddScoped<SignInService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<Gamma.Erp.Api.Setup.SetupService>();

// Files: the store and its limits from the host (Files:Store, Files:Root, Files:MaxBytes, Files:ContentTypes); on disk by default.
var filesSection = builder.Configuration.GetSection("Files");
var fileOptions = new Gamma.Erp.Services.Files.FileOptions(filesSection["Store"] ?? "Disk",
    filesSection["Root"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "files"), filesSection.GetValue<long?>("MaxBytes") ?? 10 * 1024 * 1024,
    filesSection.GetSection("ContentTypes").Get<string[]>() is { Length: > 0 } types ? new HashSet<string>(types, StringComparer.OrdinalIgnoreCase)
        : Gamma.Erp.Services.Files.FileOptions.DefaultContentTypes);
builder.Services.AddSingleton(fileOptions);
builder.Services.AddSingleton<Gamma.Erp.Services.Files.IFileStore>(fileOptions.Store switch
{
    "Disk" => new Gamma.Erp.Services.Files.DiskFileStore(fileOptions.Root!),
    var other => throw new InvalidOperationException($"Files:Store {other} is not a file store this host has (Disk)."),
});
builder.Services.AddScoped<Gamma.Erp.Services.Files.FileService>();

// The keys of the sign-in cookies: kept where the host says (DataProtection:KeysFolder, a volume in the container), so a restart or a new
// container does not sign everyone out.
if (builder.Configuration["DataProtection:KeysFolder"] is { Length: > 0 } keysFolder)
{
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysFolder)).SetApplicationName("gamma-erp");
}

// A request with a personal token (Bearer GE_…) is signed in by TokenSignIn; every other one by the sign-in page's tokens (OpenIddict).
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "Gamma";
        // An unsigned request is answered with where to sign in (RFC 9728), so Claude finds the ERP's sign-in by itself; screens get the same 401.
        options.DefaultChallengeScheme = ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationDefaults.AuthenticationScheme;
    })
    .AddMcp(Gamma.Erp.Api.Mcp.McpSetup.Describe)
    .AddPolicyScheme("Gamma", "Gamma", options => options.ForwardDefaultSelector = http =>
        Gamma.Erp.Api.Auth.TokenSignIn.Carries(http) ? Gamma.Erp.Api.Auth.TokenSignIn.SchemeName : OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, Gamma.Erp.Api.Auth.TokenSignIn>(Gamma.Erp.Api.Auth.TokenSignIn.SchemeName, null)
    .AddCookie(AuthEndpoints.LoginScheme, options =>
    {
        options.LoginPath = "/account/login";
        options.Cookie.Name = "gamma-erp-login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization();
builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AuthDbContext>())
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("connect/authorize").SetTokenEndpointUris("connect/token").SetEndSessionEndpointUris("connect/logout");
        // Both discovery addresses (MCP clients ask for the OAuth one), and where a client registers itself (Gamma.Erp.Api.Mcp.McpSetup).
        options.SetConfigurationEndpointUris(".well-known/openid-configuration", ".well-known/oauth-authorization-server");
        // The MCP endpoint's public addresses (Mcp:Resources): Claude names it as the resource it signs in for (RFC 8707).
        options.RegisterResources([.. Gamma.Erp.Api.Mcp.McpSetup.Resources(builder.Configuration)]);
        options.AddEventHandler<OpenIddict.Server.OpenIddictServerEvents.HandleConfigurationRequestContext>(handler => handler.UseInlineHandler(context =>
        {
            context.Metadata["registration_endpoint"] = new OpenIddictParameter(new Uri(context.BaseUri!, "connect/register").AbsoluteUri);
            return default;
        }));
        options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange().AllowRefreshTokenFlow();
        options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.OfflineAccess);
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(30));
        // Production keys come from the host, never from the repository: PFX files mounted into the container (Auth:SigningCertificate,
        // Auth:EncryptionCertificate, their password in Auth:CertificatePassword). Without them the keys live only as long as the process,
        // so every restart signs everyone out.
        var auth = builder.Configuration.GetSection("Auth");
        if (builder.Environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
        }
        else if (auth["SigningCertificate"] is { Length: > 0 } signing && auth["EncryptionCertificate"] is { Length: > 0 } encryption)
        {
            options.AddSigningCertificate(System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(signing, auth["CertificatePassword"]))
                .AddEncryptionCertificate(System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(encryption, auth["CertificatePassword"]));
        }
        else
        {
            options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
        }

        // HTTPS is required unless the host says otherwise (Auth:RequireHttps false: behind a proxy that ends TLS, or a local test).
        var aspnet = options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableEndSessionEndpointPassthrough();
        if (builder.Environment.IsDevelopment() || !auth.GetValue("RequireHttps", true))
        {
            aspnet.DisableTransportSecurityRequirement();
        }
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    // The sign-in, so the API reference (Scalar) and other tools can get a token: OpenID Connect, authorization code + PKCE.
    document.Components ??= new Microsoft.OpenApi.OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes[ApiReference.Scheme] = new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.SecuritySchemeType.OAuth2,
        Description = "Sign in with your Gamma ERP user (OpenID Connect, authorization code + PKCE).",
        Flows = new Microsoft.OpenApi.OpenApiOAuthFlows
        {
            AuthorizationCode = new Microsoft.OpenApi.OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri("/connect/authorize", UriKind.Relative), TokenUrl = new Uri("/connect/token", UriKind.Relative),
                Scopes = new Dictionary<string, string> { [Scopes.OpenId] = "Sign in", [Scopes.OfflineAccess] = "Stay signed in (refresh token)" },
            },
        },
    };
    document.Security = [new Microsoft.OpenApi.OpenApiSecurityRequirement { [new Microsoft.OpenApi.OpenApiSecuritySchemeReference(ApiReference.Scheme, document)] = [] }];
    return Task.CompletedTask;
}));
// Tries from one network address (Auth:SignInLimitPerMinute): on top of the lock on a user, which one address trying many names never reaches.
var signInLimit = builder.Configuration.GetValue("Auth:SignInLimitPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(AuthEndpoints.SignInLimit, http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = signInLimit, Window = TimeSpan.FromMinutes(1) }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (rejected, ct) =>
    {
        rejected.HttpContext.Response.ContentType = "text/html";
        await rejected.HttpContext.Response.WriteAsync(AuthEndpoints.TooManyTries, ct);
    };
});
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    // Behind the host's reverse proxy: the original scheme and client address. Only when the host turns it on (ForwardedHeaders:Enabled),
    // and only where the proxy is the API's only way in: otherwise anyone could claim HTTPS.
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.AddGammaMcp();

var app = builder.Build();
if (app.Configuration.GetValue("ForwardedHeaders:Enabled", false))
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<ErrorMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<RequestContextMiddleware>();
app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapApiReference();
}
app.MapHealthChecks("/health");
app.MapAuth();
app.MapApi();
app.MapGammaMcp();

// The sign-in tables are this host's own; the business schema is migrated by its own migrations (the dev seed or the deployment).
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
    await RegisterClientsAsync(scope.ServiceProvider, app.Configuration);
}

if (args.FirstOrDefault() == Gamma.Erp.Api.Setup.CreateCompanyCommand.Name)
{
    await using var command = app.Services.CreateAsyncScope();
    return await Gamma.Erp.Api.Setup.CreateCompanyCommand.RunAsync(command.ServiceProvider.GetRequiredService<Gamma.Erp.Api.Setup.SetupService>(),
        command.ServiceProvider.GetRequiredService<ErpDbContext>(), args,
        Environment.GetEnvironmentVariable(Gamma.Erp.Api.Setup.CreateCompanyCommand.PasswordSetting), Console.Out, CancellationToken.None);
}

await app.RunAsync();
return 0;

// The screens' client (public, code + PKCE), with the redirect addresses from configuration (Auth:RedirectUris).
static async Task RegisterClientsAsync(IServiceProvider services, IConfiguration configuration)
{
    var manager = services.GetRequiredService<IOpenIddictApplicationManager>();
    const string clientId = "gamma-erp-web";
    var redirects = configuration.GetSection("Auth:RedirectUris").Get<string[]>() ?? [];
    var descriptor = new OpenIddictApplicationDescriptor
    {
        ClientId = clientId, ClientType = ClientTypes.Public, DisplayName = "Gamma ERP",
        Permissions =
        {
            Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.EndSession,
            Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken, Permissions.ResponseTypes.Code,
            Permissions.Scopes.Profile, Permissions.Prefixes.Scope + Scopes.OfflineAccess,
        },
        Requirements = { Requirements.Features.ProofKeyForCodeExchange },
    };
    foreach (var uri in redirects)
    {
        descriptor.RedirectUris.Add(new Uri(uri));
        descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
    }

    if (await manager.FindByClientIdAsync(clientId) is { } existing)
    {
        await manager.UpdateAsync(existing, descriptor);
    }
    else
    {
        await manager.CreateAsync(descriptor);
    }
}

/// <summary>The host, for the API tests (WebApplicationFactory).</summary>
public partial class Program;

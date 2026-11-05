using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Gamma.Erp.Api.Auth;
using Gamma.Erp.Api.Setup;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Api.Tests;

/// <summary>The forgotten password through a real mail server: the code, its one use, its few tries, what never shows who exists; and the limit on tries from one address.</summary>
public sealed partial class PasswordResetTests : IAsyncLifetime
{
    private const string Password = "test-only-password-1";
    private const string Changed = "a-brand-new-password-2";
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly IContainer _mail = new ContainerBuilder("axllent/mailpit:v1.21.8").WithPortBinding(1025, true).WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/api/v1/messages"))).Build();
    private WebApplicationFactory<Program> _api = null!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_db.StartAsync(), _mail.StartAsync());
        var options = PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(), _db.GetConnectionString())
            .AddInterceptors(new TenantConnectionInterceptor(new TenantContext()));
        await using (var context = new ErpDbContext((DbContextOptions<ErpDbContext>)options.Options))
        {
            await context.Database.MigrateAsync();
        }

        _api = Host(mail: true, limit: 100);
    }

    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _mail.DisposeAsync();
        await _db.DisposeAsync();
    }

    private WebApplicationFactory<Program> Host(bool mail, int limit) => new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
    {
        host.UseEnvironment("Development");
        host.UseSetting("ConnectionStrings:Erp", _db.GetConnectionString());
        host.UseSetting("Auth:RedirectUris:0", "http://localhost/signed-in");
        host.UseSetting("Auth:SignInLimitPerMinute", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (mail)
        {
            host.UseSetting("Mail:Host", _mail.Hostname);
            host.UseSetting("Mail:Port", _mail.GetMappedPublicPort(1025).ToString(System.Globalization.CultureInfo.InvariantCulture));
            host.UseSetting("Mail:Security", "None");
            host.UseSetting("Mail:From", "erp@example.test");
        }
    });

    private static FormUrlEncodedContent Form(params (string Name, string Value)[] fields) => new(fields.Select(f => KeyValuePair.Create(f.Name, f.Value)));

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient browser, string username, string password) =>
        await browser.PostAsync(new Uri("/account/login", UriKind.Relative), Form(("username", username), ("password", password), ("returnUrl", "/")), TestContext.Current.CancellationToken);

    private HttpClient Browser() => _api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static bool SignedIn(HttpResponseMessage answer) => answer.Headers.Location?.ToString().Contains("error", StringComparison.Ordinal) == false;

    private async Task<JsonElement> MessagesAsync(CancellationToken ct)
    {
        using var mailbox = new HttpClient { BaseAddress = new Uri($"http://{_mail.Hostname}:{_mail.GetMappedPublicPort(8025)}") };
        return JsonDocument.Parse(await mailbox.GetStringAsync(new Uri("/api/v1/messages", UriKind.Relative), ct)).RootElement.GetProperty("messages").Clone();
    }

    // The message is sent after the answer, so the test waits for it.
    private async Task<string> CodeOfMessageAsync(int number, CancellationToken ct)
    {
        for (var tries = 0; tries < 100; tries++)
        {
            var messages = await MessagesAsync(ct);
            if (messages.GetArrayLength() >= number)
            {
                using var mailbox = new HttpClient { BaseAddress = new Uri($"http://{_mail.Hostname}:{_mail.GetMappedPublicPort(8025)}") };
                var id = messages[0].GetProperty("ID").GetString();
                var text = JsonDocument.Parse(await mailbox.GetStringAsync(new Uri($"/api/v1/message/{id}", UriKind.Relative), ct)).RootElement.GetProperty("Text").GetString()!;
                return CodePattern().Match(text).Groups[1].Value;
            }

            await Task.Delay(100, ct);
        }

        throw new TimeoutException($"Message {number} did not arrive.");
    }

    [GeneratedRegex("Your code is ([A-Z2-9]{6})")]
    private static partial Regex CodePattern();

    [Fact]
    public async Task A_code_by_mail_sets_a_new_password_once_and_the_answer_never_tells_who_exists()
    {
        var ct = TestContext.Current.CancellationToken;
        var made = await _api.Services.CreateCompanyAsync(new SignUpRequest("reset.owner", Password, "AL", "Reset Lura", Activity: "BAR",
            CashDesks: ["Arka"], Users: [new StartUser("reset.staff", Password), new StartUser("no.address", Password)]), ct);
        Guid staff, cash;
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            staff = (await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Username == "reset.staff", ct)).Id;
            cash = (await db.Set<CostCenter>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == made.Company && c.Code == "CASH1", ct)).Id;
        }

        // The administrator gives the address; nobody else does, and it must be one.
        var owner = (await _api.SignInAsync("reset.owner", Password))!;
        owner.DefaultRequestHeaders.Add(RequestContextMiddleware.NodeHeader, cash.ToString());
        (await owner.PutAsJsonAsync($"/api/v1/users/{staff}/email", new UserEmail("not an address"), ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.PutAsJsonAsync($"/api/v1/users/{staff}/email", new UserEmail("staff@example.test"), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetFromJsonAsync<UserEmail>($"/api/v1/users/{staff}/email", ct))!.Email.Should().Be("staff@example.test");

        var browser = Browser();
        (await browser.GetStringAsync(new Uri("/account/login", UriKind.Relative), ct)).Should().Contain("/account/forgot");
        async Task<string> AskAsync(string username) => await (await browser.PostAsync(new Uri("/account/forgot", UriKind.Relative), Form(("username", username)), ct)).Content.ReadAsStringAsync(ct);
        var answer = await AskAsync("reset.staff");
        answer.Should().Contain("a code is on its way");
        Normalized(await AskAsync("nobody.here"), "nobody.here").Should().Be(Normalized(answer, "reset.staff"), "the answer does not tell who exists");
        Normalized(await AskAsync("no.address"), "no.address").Should().Be(Normalized(answer, "reset.staff"), "nor who has an address");
        var code = await CodeOfMessageAsync(1, ct);
        code.Should().HaveLength(6);
        await AskAsync("reset.staff");
        await Task.Delay(500, ct);
        (await MessagesAsync(ct)).GetArrayLength().Should().Be(1, "no second message so soon, and none for the user without an address");

        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var kept = await scope.ServiceProvider.GetRequiredService<ErpDbContext>().Set<PasswordResetCode>().IgnoreQueryFilters().SingleAsync(c => c.UserId == staff, ct);
            kept.CodeHash.Should().NotContain(code, "only a salted hash of the code is kept");
            SignInService.Matches(kept.CodeHash, code).Should().BeTrue();
        }

        async Task<string> ChooseAsync(string user, string entered, string password, string confirm) => await (await browser.PostAsync(new Uri("/account/reset", UriKind.Relative),
            Form(("username", user), ("code", entered), ("password", password), ("confirm", confirm)), ct)).Content.ReadAsStringAsync(ct);

        (await ChooseAsync("reset.staff", code, Changed, "something else")).Should().Contain("different");
        (await ChooseAsync("reset.staff", code, "short", "short")).Should().Contain("at least 12");
        (await ChooseAsync("nobody.here", code, Changed, Changed)).Should().Contain("wrong or no longer works");
        (await ChooseAsync("reset.owner", code, Changed, Changed)).Should().Contain("wrong or no longer works", "a code is one user's");
        SignedIn(await SignInAsync(Browser(), "reset.staff", Password)).Should().BeTrue("nothing has changed yet");
        (await ChooseAsync("reset.staff", " " + code.ToLowerInvariant() + " ", Changed, Changed)).Should().Contain("Password changed", "spaces and small letters do not matter");
        SignedIn(await SignInAsync(Browser(), "reset.staff", Password)).Should().BeFalse("the old password is gone");
        SignedIn(await SignInAsync(Browser(), "reset.staff", Changed)).Should().BeTrue();
        (await ChooseAsync("reset.staff", code, "yet-another-password-3", "yet-another-password-3")).Should().Contain("wrong or no longer works", "a code works once");

        // Five wrong tries close a code: the right one no longer works after them.
        await AskAsync("reset.staff");
        var second = await CodeOfMessageAsync(2, ct);
        var wrong = second == "AAAAAA" ? "BBBBBB" : "AAAAAA";
        for (var i = 0; i < PasswordResetService.MaxTries; i++)
        {
            (await ChooseAsync("reset.staff", wrong, Changed, Changed)).Should().Contain("wrong or no longer works");
        }

        (await ChooseAsync("reset.staff", second, "yet-another-password-3", "yet-another-password-3")).Should().Contain("wrong or no longer works", "too many tries close the code");
        SignedIn(await SignInAsync(Browser(), "reset.staff", Changed)).Should().BeTrue();
    }

    // The page repeats the name that was typed; everything else must be the same.
    private static string Normalized(string page, string username) => page.Replace(username, "NAME", StringComparison.Ordinal);

    [Fact]
    public async Task One_address_gets_only_so_many_tries_a_minute_and_without_mail_no_link_is_offered()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var limited = Host(mail: false, limit: 3);
        var browser = limited.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        (await browser.GetStringAsync(new Uri("/account/login", UriKind.Relative), ct)).Should().NotContain("/account/forgot");
        (await browser.GetStringAsync(new Uri("/account/login?returnUrl=%2Fconnect%2Fauthorize%3Fui_locales%3Dsq", UriKind.Relative), ct)).Should()
            .Contain("Identifikohu", "the sign-in page speaks the language the browser remembers");
        (await browser.GetAsync(new Uri("/account/forgot", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        for (var i = 0; i < 3; i++)
        {
            (await browser.PostAsync(new Uri("/account/login", UriKind.Relative), Form(("username", "nobody"), ("password", "x")), ct)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        }

        var over = await browser.PostAsync(new Uri("/account/forgot", UriKind.Relative), Form(("username", "nobody")), ct);
        over.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "the sign-in and the forgotten password share the limit");
        (await over.Content.ReadAsStringAsync(ct)).Should().Contain("Too many tries");
    }
}

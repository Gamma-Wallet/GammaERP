using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Gamma.Erp.Api.Setup;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Services.Files;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Api.Tests;

/// <summary>Files through the real API host: stored and read back whole, only by their own tenant, refused when too large or of a type not accepted.</summary>
public sealed class FileTests : IAsyncLifetime
{
    private const string Password = "test-only-password-1";
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gamma-erp-files-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> _api = null!;

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        var options = PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(), _db.GetConnectionString())
            .AddInterceptors(new TenantConnectionInterceptor(new TenantContext()));
        await using (var context = new ErpDbContext((DbContextOptions<ErpDbContext>)options.Options))
        {
            await context.Database.MigrateAsync();
        }

        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Development");
            host.UseSetting("ConnectionStrings:Erp", _db.GetConnectionString());
            host.UseSetting("Auth:RedirectUris:0", "http://localhost/signed-in");
            host.UseSetting("Files:Root", _root);
            host.UseSetting("Files:MaxBytes", "1024");
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _db.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<HttpClient> OwnerOfAsync(string username, string company)
    {
        var made = await _api.Services.CreateCompanyAsync(new SignUpRequest(username, Password, "AL", company), TestContext.Current.CancellationToken);

        var browser = _api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var authorize = "/connect/authorize?client_id=gamma-erp-web&response_type=code&scope=openid%20offline_access"
            + $"&redirect_uri={Uri.EscapeDataString("http://localhost/signed-in")}&code_challenge={challenge}&code_challenge_method=S256";
        await browser.GetAsync(authorize);
        await browser.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = username, ["password"] = Password, ["returnUrl"] = authorize }));
        var code = System.Web.HttpUtility.ParseQueryString((await browser.GetAsync(authorize)).Headers.Location!.Query)["code"];
        var token = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "gamma-erp-web", ["code"] = code!, ["redirect_uri"] = "http://localhost/signed-in", ["code_verifier"] = verifier,
        }));
        token.EnsureSuccessStatusCode();
        var api = _api.CreateClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            JsonDocument.Parse(await token.Content.ReadAsStringAsync()).RootElement.GetProperty("access_token").GetString());
        return api;
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string name, string type)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(type);
        return new MultipartFormDataContent { { file, "file", name } };
    }

    [Fact]
    public async Task A_file_is_read_back_whole_as_a_download_by_its_own_tenant_only_and_a_large_or_unaccepted_one_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await OwnerOfAsync("first.files", "Dyqani Pare");
        var second = await OwnerOfAsync("second.files", "Dyqani Dyte");
        var bytes = Encoding.UTF8.GetBytes("Fatura nr. 1 — çmimi 1 200 lekë");

        var uploaded = await first.PostAsync("/api/v1/files", Upload(bytes, "..\\..\\fatura.txt", "text/plain; charset=utf-8"), ct);
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync(ct));
        var stored = (await uploaded.Content.ReadFromJsonAsync<StoredFileDto>(ct))!;
        stored.FileName.Should().Be("fatura.txt", "only the name is kept, never a path");
        stored.Size.Should().Be(bytes.Length);
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().ContainSingle().Which.Should().NotContain("fatura", "the store's key is Gamma's own");

        var download = await first.GetAsync(stored.Link, ct);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync(ct)).Should().Equal(bytes);
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        download.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");

        // B10 (C3 phase 7): the list of the files the request sees, searched by name.
        async Task<int> CountAsync(HttpClient who, string query) =>
            (await who.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/files{query}", ct)).GetProperty("total").GetInt32();
        (await first.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/files", ct)).GetProperty("items")[0].GetProperty("fileName").GetString().Should().Be("fatura.txt");
        (await CountAsync(first, "?search=FATURA")).Should().Be(1, "the name is searched case aside");
        (await CountAsync(first, "?search=nothing")).Should().Be(0);
        (await CountAsync(first, "?search=%25")).Should().Be(0, "a % typed is taken literally");
        (await CountAsync(second, "")).Should().Be(0, "another tenant lists none of them");
        (await second.GetAsync(stored.Link, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, "another tenant's file is not there for it");
        (await second.GetAsync($"{stored.Link}/info", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var program = await first.PostAsync("/api/v1/files", Upload([0x4D, 0x5A, 0x90], "setup.exe", "application/x-msdownload"), ct);
        program.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await program.Content.ReadAsStringAsync(ct)).Should().Contain("FileError").And.Contain("not accepted");

        var large = await first.PostAsync("/api/v1/files", Upload(new byte[2048], "large.pdf", "application/pdf"), ct);
        large.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await large.Content.ReadAsStringAsync(ct)).Should().Contain("larger than 1024");
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().HaveCount(1, "nothing of a refused file is kept");
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Gamma.Erp.Api.Setup;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Services.Mail;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Api.Tests;

/// <summary>A posted sale printed as PDF and sent by e-mail through a real SMTP server (Mailpit), and the reports printed.</summary>
public sealed class PrintMailTests : IAsyncLifetime
{
    private const string Password = "test-only-password-1";
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

        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Development");
            host.UseSetting("ConnectionStrings:Erp", _db.GetConnectionString());
            host.UseSetting("Auth:RedirectUris:0", "http://localhost/signed-in");
            host.UseSetting("Mail:Host", _mail.Hostname);
            host.UseSetting("Mail:Port", _mail.GetMappedPublicPort(1025).ToString(System.Globalization.CultureInfo.InvariantCulture));
            host.UseSetting("Mail:Security", "None");
            host.UseSetting("Mail:From", "erp@example.test");
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        await _db.DisposeAsync();
        await _mail.DisposeAsync();
    }

    private async Task<HttpClient> SignInAsync(string username)
    {
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
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            JsonDocument.Parse(await token.Content.ReadAsStringAsync()).RootElement.GetProperty("access_token").GetString());
        return api;
    }

    [Fact]
    public async Task A_posted_sale_prints_as_a_PDF_with_its_fonts_goes_out_by_e_mail_with_it_attached_and_the_reports_print()
    {
        var ct = TestContext.Current.CancellationToken;
        var made = await _api.Services.CreateCompanyAsync(new SignUpRequest("print.owner", Password, "AL", "Restorant Vlora", Activity: "RESTAURANT",
            CashDesks: ["Arka"]), TestContext.Current.CancellationToken);
        var company = made.Company;
        var api = await SignInAsync("print.owner");

        await using var scope = _api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var sales = await db.Set<DocumentCategory>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == company && c.Code == "SALES", ct);
        var purchases = await db.Set<DocumentCategory>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == company && c.Code == "PURCHASES", ct);
        var saleType = await db.Set<DocumentType>().IgnoreQueryFilters().SingleAsync(t => t.OwnerId == company && t.Code == "FSHM", ct);
        var income = await db.Set<Chart>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == company && c.Code == "705", ct);
        var line = await db.Set<TransactionType>().IgnoreQueryFilters().SingleAsync(t => t.OwnerId == company && t.Code == "ACCOUNT", ct);
        var vat = await db.Set<VatGroup>().IgnoreQueryFilters().SingleAsync(v => v.OwnerId == company && v.Rate == 20, ct);
        var cash = await db.Set<Gamma.Erp.Models.CostCenters.CostCenter>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == company && c.Code == "CASH1", ct);
        var partner = await (await api.PostAsJsonAsync("/api/v1/partners", new ElementDto
        {
            Name = new() { ["sq"] = "Hotel Vlora" }, TypeId = (await db.Set<PartnerType>().IgnoreQueryFilters().SingleAsync(t => t.OwnerId == company && t.Code == "LTD", ct)).Id,
            CategoryIds = [(await db.Set<PartnerCategory>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == company && c.Code == "CLIENTS", ct)).Id],
        }, ct)).Content.ReadFromJsonAsync<ElementDto>(ct);
        var saved = await (await api.PostAsJsonAsync("/api/v1/documents", new DocumentDto
        {
            TypeId = saleType.Id, CategoryIds = [sales.Id], DocumentDate = new DateOnly(2026, 10, 30), PostingDate = new DateOnly(2026, 10, 30),
            FromId = cash.Id, FromName = "Arka", ToId = partner!.Id, ToName = "Hotel Vlora",
            Transactions = [new TransactionDto { TypeId = line.Id, ElementId = income.Id, ElementTypeId = income.TypeId, ElementName = "Darkë për 4 — çmimi përfshin shërbimin",
                Price = 1000, VatGroupId = vat.Id }],
        }, ct)).Content.ReadFromJsonAsync<DocumentDto>(ct);
        (await api.PostAsync($"/api/v1/documents/{saved!.Id}/post", null, ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var pdf = await api.GetAsync($"/api/v1/documents/{saved.Id}/pdf?language=sq", ct);
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        pdf.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("FSHM-FSHM0.pdf");
        var bytes = await pdf.Content.ReadAsByteArrayAsync(ct);
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        using (var read = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(bytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
        {
            var fonts = read.Pages[0].Resources.Elements.GetDictionary("/Font")!.Elements.Values
                .Select(f => ((PdfSharp.Pdf.PdfDictionary)((PdfSharp.Pdf.Advanced.PdfReference)f!).Value).Elements.GetName("/BaseFont")).ToList();
            fonts.Should().NotBeEmpty().And.OnlyContain(f => f.Contains("+DejaVu Sans"), "the font is embedded (a subset), so ë, ç and Greek print on any host");
        }

        foreach (var report in new[] { "trial-balance/pdf?from=2026-10-01&to=2026-10-31&language=sq",
            $"vat-return/pdf?output={sales.Id}&input={purchases.Id}&from=2026-10-01&to=2026-10-31&language=sq", $"vat-register/pdf?category={sales.Id}&from=2026-10-01&to=2026-10-31" })
        {
            var printed = await api.GetAsync($"/api/v1/reports/{report}", ct);
            printed.StatusCode.Should().Be(HttpStatusCode.OK, report);
            Encoding.ASCII.GetString(await printed.Content.ReadAsByteArrayAsync(ct), 0, 5).Should().Be("%PDF-", report);
        }

        // B8 / B9 (C3 phase 6): every report has its Excel sheet and its PDF.
        const string period = "from=2026-10-01&to=2026-10-31";
        foreach (var (url, header) in new[]
        {
            ($"reports/stock/pdf?language=sq", "%PDF-"), ($"reports/item-tracking/pdf?product={Guid.CreateVersion7()}&{period}", "%PDF-"),
            ($"reports/lines/pdf?category={sales.Id}&{period}", "%PDF-"), ($"partners/{partner.Id}/ledger/pdf?{period}&language=sq", "%PDF-"),
            ($"cost-centers/{cash.Id}/ledger/pdf?{period}", "%PDF-"), ($"statistics/partners/pdf?category={sales.Id}&{period}", "%PDF-"),
            ($"reports/vat-return/xlsx?output={sales.Id}&input={purchases.Id}&{period}", "PK"), ($"partners/{partner.Id}/ledger/xlsx?{period}", "PK"),
            ($"cost-centers/{cash.Id}/ledger/xlsx?{period}", "PK"), ($"statistics/partners/xlsx?category={sales.Id}&{period}", "PK"),
        })
        {
            var exported = await api.GetAsync($"/api/v1/{url}", ct);
            exported.StatusCode.Should().Be(HttpStatusCode.OK, url);
            var content = await exported.Content.ReadAsByteArrayAsync(ct);
            Encoding.ASCII.GetString(content, 0, header.Length).Should().Be(header, url);
            content.Length.Should().BeGreaterThan(1000, $"{url} holds its report");
        }

        var sent = await api.PostAsJsonAsync($"/api/v1/documents/{saved.Id}/email", new SendDocumentRequest(["klienti@example.test"], "Fatura juaj", "Faleminderit!", "sq"), ct);
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync(ct));
        using var mailbox = new HttpClient { BaseAddress = new Uri($"http://{_mail.Hostname}:{_mail.GetMappedPublicPort(8025)}") };
        var messages = JsonDocument.Parse(await mailbox.GetStringAsync("/api/v1/messages", ct)).RootElement.GetProperty("messages");
        messages.GetArrayLength().Should().Be(1);
        var message = messages[0];
        message.GetProperty("To")[0].GetProperty("Address").GetString().Should().Be("klienti@example.test");
        message.GetProperty("From").GetProperty("Address").GetString().Should().Be("erp@example.test");
        message.GetProperty("Subject").GetString().Should().Be("Fatura juaj");
        message.GetProperty("Attachments").GetInt32().Should().Be(1);

        var hidden = await api.PostAsJsonAsync($"/api/v1/documents/{saved.Id}/email", new SendDocumentRequest(["klienti@example.test, tjeter@example.test"]), ct);
        hidden.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await hidden.Content.ReadAsStringAsync(ct)).Should().Contain("MailError");
        var many = await api.PostAsJsonAsync($"/api/v1/documents/{saved.Id}/email",
            new SendDocumentRequest([.. Enumerable.Range(1, MailService.MaxRecipients + 1).Select(i => $"k{i}@example.test")]), ct);
        many.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        JsonDocument.Parse(await mailbox.GetStringAsync("/api/v1/messages", ct)).RootElement.GetProperty("messages").GetArrayLength().Should().Be(1, "nothing refused went out");
    }
}

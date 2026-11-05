using System.Text.Json.Nodes;
using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Configuration;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>The configuration rows through one service (H3): read by any user, written by an administrator, links only to the company's rows, history kept.</summary>
public sealed class ConfigurationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        await using var context = Context(new TenantContext());
        await context.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private ErpDbContext Context(TenantContext tenant) => new((DbContextOptions<ErpDbContext>)PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(),
        _db.GetConnectionString()).AddInterceptors(new TenantConnectionInterceptor(tenant), new HistoryInterceptor(tenant, TimeProvider.System)).Options);

    private async Task<(OrganisationIds Company, Guid User)> CompanyAsync(string code, CancellationToken ct)
    {
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup(code, code, "AL", [("cost_center_type:WAREHOUSE", "WH", "Magazina")]);
        await using var db = Context(new TenantContext());
        var company = Organisation.CreateCompany(db, tenantId, setup);
        Organisation.SetUpCompany(db, company, setup);
        var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
        db.Add(staff);
        await db.SaveChangesAsync(ct);
        var owner = new User { TypeId = staff.Id, Code = "OWNER", Name = TranslatedText.English("Owner"), TenantId = tenantId };
        db.Add(owner);
        await db.SaveChangesAsync(ct);
        return (company, owner.Id);
    }

    [Fact]
    public async Task Configuration_rows_are_read_by_users_written_by_administrators_linked_only_to_the_companys_rows_and_kept_with_their_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var (company, user) = await CompanyAsync("CO", ct);
        var (other, otherUser) = await CompanyAsync("OTHER", ct);
        Guid otherVat;
        var otherTenant = new TenantContext();
        otherTenant.Set(other.TenantId, otherUser);
        await using (var otherDb = Context(otherTenant))
        {
            otherVat = (await otherDb.Set<VatGroup>().FirstAsync(ct)).Id;
        }

        var tenant = new TenantContext();
        tenant.Set(company.TenantId, user);
        var admin = new RequestContext(company.TenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        var plain = admin with { IsAdministrator = false };
        await using var context = Context(tenant);
        var config = new ConfigurationService(context, admin, new Visibility(context, admin));

        var groups = await config.ListAsync("vat-groups", new Dictionary<string, string>(), ct);
        groups.Should().Contain(g => g["rate"]!.GetValue<decimal>() == 20);
        (await config.ListAsync("vat-groups", new Dictionary<string, string> { ["rate"] = "20" }, ct)).Should().ContainSingle();

        var created = await config.SaveAsync("vat-groups", new JsonObject { ["code"] = "R8", ["name"] = new JsonObject { ["en"] = "8 %", ["sq"] = "8 %" }, ["rate"] = 8 }, ct);
        var id = Guid.Parse(created["id"]!.GetValue<string>());
        (created["ownerId"]!.GetValue<string>(), created["rate"]!.GetValue<decimal>()).Should().Be((company.Corporate.ToString(), 8m), "the row is the company's");
        var changed = await config.SaveAsync("vat-groups", new JsonObject { ["id"] = id.ToString(), ["rate"] = 9 }, ct);
        (changed["rate"]!.GetValue<decimal>(), changed["code"]!.GetValue<string>()).Should().Be((9m, "R8"), "a change keeps the columns it does not name");
        (await context.Set<VatGroup>().IgnoreQueryFilters().CountAsync(v => v.Code == "R8" && !v.Active, ct)).Should().Be(1, "the old row is kept as history");

        var asUser = new ConfigurationService(context, plain, new Visibility(context, plain));
        (await asUser.ListAsync("vat-groups", new Dictionary<string, string>(), ct)).Should().NotBeEmpty("any user reads the configuration");
        var refused = async () => await asUser.SaveAsync("vat-groups", new JsonObject { ["rate"] = 5 }, ct);
        (await refused.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.NotAllowedError);

        var sale = (await context.Set<DocumentType>().SingleAsync(t => t.Code == "FSHM", ct)).Id;
        var nowhere = async () => await config.SaveAsync("document-type-vat-groups", new JsonObject { ["documentTypeId"] = sale.ToString(), ["vatGroupId"] = Guid.CreateVersion7().ToString() }, ct);
        (await nowhere.Should().ThrowAsync<ValidationException>()).Which.Errors["vatGroupId"].Should().StartWith(ErrorCodes.NotConfiguredError);
        var foreign = async () => await config.SaveAsync("document-type-vat-groups", new JsonObject { ["documentTypeId"] = sale.ToString(), ["vatGroupId"] = otherVat.ToString() }, ct);
        (await foreign.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("vatGroupId", "another tenant's row is not this company's");
        var linked = await config.SaveAsync("document-type-vat-groups", new JsonObject { ["documentTypeId"] = sale.ToString(), ["vatGroupId"] = id.ToString() }, ct);
        (await config.ListAsync("document-type-vat-groups", new Dictionary<string, string> { ["vatGroupId"] = id.ToString() }, ct)).Should().ContainSingle();

        var unknown = async () => await config.SaveAsync("vat-groups", new JsonObject { ["password"] = "x" }, ct);
        (await unknown.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("password");
        var noModel = async () => await config.ListAsync("users", new Dictionary<string, string>(), ct);
        await noModel.Should().ThrowAsync<ValidationException>("users are not configuration rows");

        (await config.DeleteAsync("document-type-vat-groups", Guid.Parse(linked["id"]!.GetValue<string>()), ct)).Should().BeTrue();
        (await config.ListAsync("document-type-vat-groups", new Dictionary<string, string> { ["vatGroupId"] = id.ToString() }, ct)).Should().BeEmpty("switched off");

        // A document type's own settings: never posted, and its reversal (the database keeps one reversal type per type).
        var services = await context.Set<DocumentType>().SingleAsync(t => t.Code == "FSHSH", ct);
        await config.SetDocumentTypeAsync(services.Id, new DocumentTypeSettings(true, services.AllowsPartialReversal, false, null), ct);
        var client = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id, Code = "C1", Name = TranslatedText.English("Client"),
            TenantId = company.TenantId, OwnerId = company.Corporate };
        context.Add(client);
        await context.SaveChangesAsync(ct);
        var sales = await context.Set<DocumentCategory>().SingleAsync(c => c.Code == "SALES", ct);
        var account = await context.Set<Chart>().SingleAsync(c => c.Code == "704", ct);
        var documents = new DocumentService(context, admin, new Visibility(context, admin), TimeProvider.System);
        var warehouse = await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH", ct);
        var draft = await documents.SaveAsync(new DocumentDto
        {
            TypeId = services.Id, CategoryIds = [sales.Id], DocumentDate = new DateOnly(2026, 10, 30), PostingDate = new DateOnly(2026, 10, 30), FromId = warehouse.Id, ToId = client.Id,
            Transactions = [new TransactionDto { TypeId = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT", ct)).Id, ElementId = account.Id,
                ElementTypeId = account.TypeId, ElementName = "Shërbim", Price = 100, VatGroupId = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id }],
        }, ct);
        var post = async () => await documents.PostAsync(draft.Id!.Value, ct);
        (await post.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.NeverPostedError, "the administrator said this type never posts");

        var creditNote = await context.Set<DocumentType>().SingleAsync(t => t.Code == "FKBM", ct);
        var second = async () => await config.SetDocumentTypeAsync(creditNote.Id, new DocumentTypeSettings(false, false, true, sale), ct);
        (await second.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain("ux_document_type_reversed", "FSHM already has its reversal type");
    }

    [Fact]
    public async Task A_models_schema_and_link_choices_come_from_the_database_model_and_a_document_types_settings_and_users_read_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var (company, user) = await CompanyAsync("SCH", ct);
        var tenant = new TenantContext();
        tenant.Set(company.TenantId, user);
        var admin = new RequestContext(company.TenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var config = new ConfigurationService(context, admin, new Visibility(context, admin));

        // B6: columns by kind, the system's own columns left out, links named by the model they point at.
        var vat = config.Schema("vat-groups");
        vat.Columns.Select(c => (c.Name, c.Kind)).Should().Contain([("code", "text"), ("name", "translated"), ("rate", "number"), ("depreciation", "boolean")]);
        vat.Columns.Select(c => c.Name).Should().NotContain(["id", "ownerId", "tenantId", "active", "idChange", "createdOn"]);
        var links = config.Schema("document-type-vat-groups").Columns;
        links.Should().Contain(c => c.Name == "vatGroupId" && c.IsLink && c.LinkModel == "vat-groups");
        links.Should().Contain(c => c.Name == "documentTypeId" && c.IsLink && c.LinkModel == null, "a document type is not a configuration model");

        var choices = await config.ChoicesAsync("document-type-vat-groups", "vatGroupId", null, "en", ct);
        choices.Should().Contain(c => c.Name.Length > 0).And.HaveCount(await context.Set<VatGroup>().CountAsync(ct));
        var twenty = await context.Set<VatGroup>().FirstAsync(v => v.Rate == 20, ct);
        (await config.ChoicesAsync("document-type-vat-groups", "vatGroupId", twenty.Code, "en", ct)).Should().Contain(c => c.Id == twenty.Id);
        // A choice is never an id: a row with no name goes by what it links to, an entity scope by its name, category and type.
        var under = await config.ChoicesAsync("transaction-type-scopes", "idSup", null, "en", ct);
        under.Should().NotBeEmpty();
        under.Where(c => Guid.TryParse(c.Name, out _)).Should().BeEmpty();
        under.Should().Contain(c => c.Name.Contains("Staff"));
        under.Select(c => c.Name).Should().Contain(n => n.StartsWith("Item · ", StringComparison.Ordinal)).And.Contain(n => n.StartsWith("Fixed asset · ", StringComparison.Ordinal), "the line type tells two scopes on one entity apart");
        var scopes = await config.ChoicesAsync("transaction-type-scopes", "elementsId", null, "en", ct);
        scopes.Should().NotBeEmpty();
        scopes.Where(c => Guid.TryParse(c.Name, out _)).Should().BeEmpty();
        scopes.Should().Contain(c => c.Name.StartsWith("Accounts · ", StringComparison.Ordinal), "the category tells them apart");
        var notALink = () => config.ChoicesAsync("vat-groups", "rate", null, "en", ct);
        (await notALink.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("rate");

        // B7: a document type's settings and users read back as written.
        var sale = await context.Set<DocumentType>().SingleAsync(t => t.Code == "FSHM", ct);
        (await config.DocumentTypeAsync(sale.Id, ct))!.NeverPosted.Should().BeFalse();
        await config.SetDocumentTypeAsync(sale.Id, new DocumentTypeSettings(NeverPosted: false, AllowsPartialReversal: true, IsReversal: false, null), ct);
        (await config.DocumentTypeAsync(sale.Id, ct))!.AllowsPartialReversal.Should().BeTrue();
        await new DocumentService(context, admin, new Visibility(context, admin), TimeProvider.System).SaveTypeUsersAsync(sale.Id, [user], [], ct);
        (await config.DocumentTypeUsersAsync(sale.Id, ct)).UserIds.Should().Equal(user);
        (await config.DocumentTypeAsync(Guid.CreateVersion7(), ct)).Should().BeNull();
    }
}

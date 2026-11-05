using System.Text.Json.Nodes;
using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>Offer to contract to invoice, offer to invoice, contract to invoice (the founder, 2026-11-01): what a document may make is configuration, its properties travel with it.</summary>
public sealed class DocumentFlowTests : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 10, 1);
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

    [Fact]
    public async Task An_offer_is_taken_up_once_a_contract_bills_again_and_again_until_its_end_date()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "Dyqani", "AL", [("cost_center_type:WAREHOUSE", "WH", "Magazina")]);
        OrganisationIds company;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            await db.SaveChangesAsync(ct);
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, null);
        await using var context = Context(tenant);
        var owner = company.Corporate;
        var request = new RequestContext(tenantId, null, owner, new HashSet<Guid>(), IsAdministrator: true);
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var types = await context.Set<DocumentType>().ToListAsync(ct);
        DocumentType Named(string name) => types.Single(t => t.Name.In("en") == name);
        var (offer, contract, invoice, purchase) = (Named("Sales offer, goods"), Named("Sales contract, goods"), Named("Sales invoice, goods"), Named("Purchase invoice, goods"));
        async Task<Guid> CategoryOf(Guid type) => await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == type).Select(l => l.CategoryId).FirstAsync(ct);

        var warehouse = (await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH", ct)).Id;
        var sme = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "SME", ct)).Id;
        var (client, seller) = (new Partner { TypeId = sme, Code = "C1", Name = TranslatedText.English("Klienti"), TenantId = tenantId, OwnerId = owner },
            new Partner { TypeId = sme, Code = "S1", Name = TranslatedText.English("Furnitori"), TenantId = tenantId, OwnerId = owner });
        var goods = await context.Set<ProductCategory>().SingleAsync(c => c.Code == "PRODUCT", ct);
        var goodsType = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == goods.Id).Select(l => l.TypeId).FirstAsync(ct);
        var oil = new Product { TypeId = goodsType, Code = "OIL", Name = TranslatedText.English("Vaj"), TenantId = tenantId, OwnerId = owner };
        context.AddRange(client, seller, oil, new InterProductCategory { ElementId = oil.Id, CategoryId = goods.Id, TenantId = tenantId, OwnerId = owner });
        await context.SaveChangesAsync(ct);
        var item = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ITEM", ct)).Id;
        var unit = (await context.Set<Unit>().SingleAsync(u => u.Code == "PIECE", ct)).Id;
        var vat = (await context.Set<VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id;
        TransactionDto Line(decimal quantity, decimal price) => new() { TypeId = item, ElementId = oil.Id, ElementName = "Vaj", UnitId = unit, Quantity = quantity, Price = price, VatGroupId = vat };
        await documents.PostAsync((await documents.SaveAsync(new DocumentDto
        {
            TypeId = purchase.Id, CategoryIds = [await CategoryOf(purchase.Id)], PostingDate = Day, FromId = seller.Id, ToId = warehouse, Transactions = [Line(100, 1)],
        }, ct)).Id!.Value, ct);

        var theOffer = await documents.SaveAsync(new DocumentDto
        {
            TypeId = offer.Id, CategoryIds = [await CategoryOf(offer.Id)], DocumentDate = Day, FromId = warehouse, ToId = client.Id, ToName = "Klienti", Transactions = [Line(3, 10)],
        }, ct);

        // What it may make is the flow's: a contract or an invoice of its kind, nothing else.
        var flows = await documents.FlowsAsync(theOffer.Id!.Value, "en", ct);
        flows.Select(f => f.TypeId).Should().BeEquivalentTo([contract.Id, invoice.Id]);
        flows.Should().OnlyContain(f => f.Refused == null);
        var other = async () => await documents.MakeAsync(theOffer.Id!.Value, purchase.Id, ct);
        (await other.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.NotConfiguredError);

        // An unposted copy, its lines and partner the offer's, linked to it.
        var made = await documents.MakeAsync(theOffer.Id!.Value, contract.Id, ct);
        (made.TypeId, made.Posted, made.ToId).Should().Be((contract.Id, false, client.Id));
        made.CreatedFromIds.Should().Equal(theOffer.Id!.Value);
        made.Transactions.Should().ContainSingle().Which.Should().Match<TransactionDto>(t => t.ElementId == oil.Id && t.Quantity == 3 && t.Price == 10 && t.Id != theOffer.Transactions[0].Id);

        // An offer is taken up once per type; the other way is still open.
        var again = async () => await documents.MakeAsync(theOffer.Id!.Value, contract.Id, ct);
        (await again.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain("already made");
        var after = await documents.FlowsAsync(theOffer.Id!.Value, "en", ct);
        after.Single(f => f.TypeId == contract.Id).Refused.Should().NotBeNull();
        after.Single(f => f.TypeId == invoice.Id).Refused.Should().BeNull();
        (await documents.MakeAsync(theOffer.Id!.Value, invoice.Id, ct)).TypeId.Should().Be(invoice.Id);

        // A contract bills again and again, month after month.
        var theContract = made.Id!.Value;
        var first = await documents.MakeAsync(theContract, invoice.Id, ct);
        await documents.MakeAsync(theContract, invoice.Id, ct);
        (await documents.MadeFromAsync(theContract, "en", ct)).Select(m => m.Type).Should().AllBe(invoice.Name.In("en")).And.HaveCount(2);
        (await documents.PostAsync(first.Id!.Value, ct)).Posted.Should().BeTrue("the invoice posts like any other");

        // The end date is a property of the contract: saved with it, and past it the contract makes nothing more.
        var end = (await context.Set<DocumentTypeInfo>().Where(i => i.TypeId == contract.Id).ToListAsync(ct)).Single(i => i.Name.In("en") == "End date");
        var full = (await documents.DocumentAsync(theContract, ct))!;
        await documents.SaveAsync(full with { Values = new() { [end.Id] = JsonValue.Create("2020-12-31") } }, ct);
        (await documents.DocumentAsync(theContract, ct))!.Values.Should().ContainKey(end.Id);
        var ended = async () => await documents.MakeAsync(theContract, invoice.Id, ct);
        (await ended.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain("ended on 31/12/2020");
        (await documents.FlowsAsync(theContract, "en", ct)).Single().Refused.Should().Contain("ended");

        // The contracts report: what it is worth, what was invoiced (posted only, less reversals), what remains, when it ends.
        var reports = new ReportService(context, new Visibility(context, request));
        var (always, ever) = (new DateOnly(2000, 1, 1), new DateOnly(2100, 1, 1));
        async Task<ContractRowDto> ContractAsync() => (await reports.ContractsAsync([await CategoryOf(contract.Id)], client.Id, always, ever, "en", ct)).Single(r => r.DocumentId == theContract);
        var row = await ContractAsync();
        (row.Partner, row.Value, row.Invoiced, row.Remaining, row.ValidUntil).Should().Be(("Klienti", 36m, 36m, 0m, new DateOnly(2020, 12, 31)), "one invoice posted, the other still a draft");
        (await reports.ContractsAsync([await CategoryOf(contract.Id)], seller.Id, always, ever, "en", ct)).Should().BeEmpty("another client's");
        context.ChangeTracker.Clear();
        await documents.ReverseAsync(first.Id!.Value, null, first.PostingDate!.Value, ct);
        row = await ContractAsync();
        (row.Invoiced, row.Remaining).Should().Be((0m, 36m), "a reversed invoice leaves nothing invoiced");

        // The other document must hold the same kinds of rows: a service row can't go on a goods invoice, a goods contract holds it.
        var services = await context.Set<ProductCategory>().SingleAsync(c => c.Code == "SERVICE", ct);
        var serviceType = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == services.Id).Select(l => l.TypeId).FirstAsync(ct);
        var delivery = new Product { TypeId = serviceType, Code = "DELIVERY", Name = TranslatedText.English("Transport"), TenantId = tenantId, OwnerId = owner };
        context.AddRange(delivery, new InterProductCategory { ElementId = delivery.Id, CategoryId = services.Id, TenantId = tenantId, OwnerId = owner });
        await context.SaveChangesAsync(ct);
        var serviceLine = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "SERVICE_ITEM", ct)).Id;
        var mixed = await documents.SaveAsync(new DocumentDto
        {
            TypeId = offer.Id, CategoryIds = [await CategoryOf(offer.Id)], DocumentDate = Day, FromId = warehouse, ToId = client.Id, ToName = "Klienti",
            Transactions = [Line(1, 10), new TransactionDto { TypeId = serviceLine, ElementId = delivery.Id, ElementName = "Transport", UnitId = unit, Quantity = 1, Price = 5, VatGroupId = vat }],
        }, ct);
        var open = await documents.FlowsAsync(mixed.Id!.Value, "en", ct);
        open.Single(f => f.TypeId == invoice.Id).Refused.Should().Contain("has no");
        open.Single(f => f.TypeId == contract.Id).Refused.Should().BeNull();
        var noRows = async () => await documents.MakeAsync(mixed.Id!.Value, invoice.Id, ct);
        (await noRows.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain("has no");
        var open2 = await documents.MakeAsync(mixed.Id!.Value, contract.Id, ct);
        open2.Transactions.Should().HaveCount(2);
        (await reports.ContractsAsync([await CategoryOf(contract.Id)], null, always, ever, "en", ct)).Single(r => r.DocumentId == open2.Id).ValidUntil.Should().BeNull("no end date given");

        // Configuring a flow: the target must have the source's transactions.
        var config = new Gamma.Erp.Services.Configuration.ConfigurationService(context, request, new Visibility(context, request));
        var wrong = async () => await config.SaveAsync("document-type-flows", new JsonObject { ["sourceTypeId"] = offer.Id.ToString(), ["targetTypeId"] = invoice.Id.ToString() }, ct);
        (await wrong.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("targetTypeId");
        (await config.SaveAsync("document-type-flows", new JsonObject { ["sourceTypeId"] = invoice.Id.ToString(), ["targetTypeId"] = offer.Id.ToString() }, ct))["once"]!.GetValue<bool>().Should().BeFalse();

        // Offers and contracts are never posted, so they have nothing to reverse, and no reversal type.
        types.Should().NotContain(t => t.ReversedDocumentTypeId == offer.Id || t.ReversedDocumentTypeId == contract.Id);
    }
}

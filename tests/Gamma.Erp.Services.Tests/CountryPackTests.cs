using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Posting;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>Every country's pack loads onto a company of its own and posts a sale of goods on its own chart, balanced.</summary>
public sealed class CountryPackTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public static TheoryData<string> Countries => [.. StarterPacks.Countries];

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        await using var context = Context(new TenantContext());
        await context.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private ErpDbContext Context(TenantContext tenant) => new((DbContextOptions<ErpDbContext>)PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(),
        _db.GetConnectionString()).AddInterceptors(new TenantConnectionInterceptor(tenant), new HistoryInterceptor(tenant, TimeProvider.System)).Options);

    /// <summary>The registers rights are given on: each through its own elements row (no category, type or record).</summary>
    private static readonly string[] Registers =
        ["tbl_partner", "tbl_product", "tbl_cost_center", "tbl_chart", "tbl_unit", "tbl_employee", "tbl_user", "tbl_document", "tbl_transaction"];

    [Theory]
    [MemberData(nameof(Countries))]
    public async Task A_countrys_pack_gives_every_register_its_own_elements_row_so_rights_can_be_given_on_it(string country)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "A company", country, [("cost_center_type:WAREHOUSE", "WH", "Warehouse")]);
        await using var db = Context(new TenantContext());
        var company = Organisation.CreateCompany(db, tenantId, setup);
        Organisation.SetUpCompany(db, company, setup);
        await db.SaveChangesAsync(ct);
        var registers = await db.Set<Elements>().IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.Active && e.Table != null && e.CategoryId == null && e.TypeId == null && e.RecordId == null)
            .Select(e => e.Table!).Distinct().ToListAsync(ct);
        registers.Should().Contain(Registers, $"{country}'s pack names every register as a whole");
    }

    [Theory]
    [MemberData(nameof(Countries))]
    public async Task A_countrys_pack_posts_a_sale_of_goods_on_its_own_chart_balanced(string country)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "A company", country, [("cost_center_type:WAREHOUSE", "WH", "Warehouse")]);
        OrganisationIds company;
        Guid user;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            db.Add(staff);
            await db.SaveChangesAsync(ct);
            var owner = new User { TypeId = staff.Id, Code = "OWNER", Name = TranslatedText.English("Owner"), TenantId = tenantId };
            db.Add(owner);
            await db.SaveChangesAsync(ct);
            user = owner.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var client = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id, Code = "C1", Name = TranslatedText.English("Client"),
            TenantId = tenantId, OwnerId = company.Corporate };
        context.Add(client);
        await context.SaveChangesAsync(ct);

        var sales = await context.Set<DocumentCategory>().SingleAsync(c => c.Code == "SALES", ct);
        var inSales = context.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == sales.Id).Select(l => l.TypeId);
        var type = (await context.Set<DocumentType>().Where(t => inSales.Contains(t.Id)).ToListAsync(ct)).Single(t => t.Name.In("en") == "Sales invoice, goods");
        var account = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT", ct)).Id;
        // The income account a sale's line takes: an account (no accounts under it) of the class its scenario is scoped on.
        var link = await context.Set<InterDocumentTypeTransactionType>().SingleAsync(l => l.DocumentTypeId == type.Id && l.TransactionTypeId == account, ct);
        var scopes = context.Set<Scenario>().Where(s => s.DocumentTypeTransactionTypeId == link.Id).Select(s => (Guid?)s.ElementsId);
        var incomeClass = (await context.Set<Elements>().SingleAsync(e => scopes.Contains(e.Id), ct)).TypeId;
        var parents = context.Set<Chart>().Where(c => c.IdSup != null).Select(c => c.IdSup);
        var income = await context.Set<Chart>().Where(c => c.TypeId == incomeClass && !parents.Contains(c.Id)).OrderBy(c => c.Code).FirstAsync(ct);
        var vat = await context.Set<VatGroup>().OrderByDescending(v => v.Rate).FirstAsync(ct);
        var warehouse = await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH", ct);
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);

        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = type.Id, CategoryIds = [sales.Id], DocumentDate = new DateOnly(2026, 10, 29), PostingDate = new DateOnly(2026, 10, 29),
            FromId = warehouse.Id, FromName = "Warehouse", ToId = client.Id, ToName = "Client",
            Transactions = [new TransactionDto { TypeId = account, ElementId = income.Id, ElementTypeId = income.TypeId, ElementName = "Goods", Price = 100, VatGroupId = vat.Id }],
        }, ct);
        await documents.PostAsync(saved.Id!.Value, ct);

        var entries = await context.Set<ChartValue>().Where(v => v.DocumentId == saved.Id).ToListAsync(ct);
        entries.Should().HaveCount(3, $"{country}: the income, the output VAT and the client's receivable");
        (entries.Sum(e => e.Debit), entries.Sum(e => e.Credit)).Should().Be((100 + vat.Rate, 100 + vat.Rate), $"{country}: 100 + VAT {vat.Rate} % on each side");
        entries.Should().ContainSingle(e => e.ChartId == income.Id && e.Credit == 100);
    }

    [Theory]
    [MemberData(nameof(Countries))]
    public async Task A_countrys_pack_posts_cash_and_bank_on_the_accounts_its_cash_desk_and_bank_hold_and_never_posts_an_offer(string country)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "A company", country, [("cost_center_type:CASH_DESK", "CASH", "Cash desk"), ("cost_center_type:BANK", "BANK", "Bank")]);
        OrganisationIds company;
        Guid user;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            db.Add(staff);
            await db.SaveChangesAsync(ct);
            await Organisation.CompleteCostCentersAsync(db, company.Corporate, ct);
            var owner = new User { TypeId = staff.Id, Code = "OWNER", Name = TranslatedText.English("Owner"), TenantId = tenantId };
            db.Add(owner);
            await db.SaveChangesAsync(ct);
            user = owner.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var ltd = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id;
        var client = new Partner { TypeId = ltd, Code = "C1", Name = TranslatedText.English("Client"), TenantId = tenantId, OwnerId = company.Corporate };
        var supplier = new Partner { TypeId = ltd, Code = "S1", Name = TranslatedText.English("Supplier"), TenantId = tenantId, OwnerId = company.Corporate };
        context.AddRange(client, supplier);
        await context.SaveChangesAsync(ct);
        var cash = await context.Set<CostCenter>().SingleAsync(c => c.Code == "CASH", ct);
        var bank = await context.Set<CostCenter>().SingleAsync(c => c.Code == "BANK", ct);
        var held = await context.Set<CostCenterInfo>().Where(i => i.ElementId == cash.Id || i.ElementId == bank.Id).ToListAsync(ct);
        var accounts = await context.Set<Chart>().ToDictionaryAsync(c => c.Id.ToString(), c => c.Id, ct);
        Guid Holds(CostCenter centre) => held.Where(i => i.ElementId == centre.Id).Select(i => i.Value?.GetValue<string>()).OfType<string>()
            .Where(accounts.ContainsKey).Select(v => accounts[v]).Single();
        var (cashAccount, bankAccount) = (Holds(cash), Holds(bank));
        var types = await context.Set<DocumentType>().ToListAsync(ct);
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var date = new DateOnly(2026, 10, 30);

        async Task<(Guid Id, List<ChartValue> Entries)> PostAsync(string typeName, (Guid Id, string Name) from, (Guid Id, string Name) to, string kind, (Guid Id, Guid TypeId) element,
            decimal amount)
        {
            var type = types.Single(t => t.IdSup == null && t.Name.In("en") == typeName);
            var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == type.Id).Select(l => l.CategoryId).FirstAsync(ct);
            var lineType = (await context.Set<TransactionType>().SingleAsync(t => t.Code == kind, ct)).Id;
            var vat = await (from l in context.Set<InterDocumentTypeVatGroup>() join v in context.Set<VatGroup>() on l.VatGroupId equals v.Id
                             where l.DocumentTypeId == type.Id orderby v.Rate select (Guid?)v.Id).FirstOrDefaultAsync(ct);
            var saved = await documents.SaveAsync(new DocumentDto
            {
                TypeId = type.Id, CategoryIds = [category], DocumentDate = date, PostingDate = date, FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name,
                Transactions = [new TransactionDto { TypeId = lineType, ElementId = element.Id, ElementTypeId = element.TypeId, ElementName = kind, Price = amount, VatGroupId = vat }],
            }, ct);
            await documents.PostAsync(saved.Id!.Value, ct);
            return (saved.Id!.Value, await context.Set<ChartValue>().Where(v => v.DocumentId == saved.Id).ToListAsync(ct));
        }

        var byCash = await PostAsync("Receipt", (client.Id, "Client"), (cash.Id, "Cash desk"), "CASH_DESK_ACCOUNT", (cash.Id, cash.TypeId), 500);
        byCash.Entries.Should().HaveCount(2);
        byCash.Entries.Should().ContainSingle(e => e.ChartId == cashAccount && e.Debit == 500, $"{country}: the money in the account the cash desk holds");
        byCash.Entries.Should().ContainSingle(e => e.ChartId != cashAccount && e.Credit == 500, $"{country}: against the client");

        var byClient = await PostAsync("Receipt", (client.Id, "Client"), (cash.Id, "Cash desk"), "CLIENT", (client.Id, client.TypeId), 300);
        byClient.Entries.Should().ContainSingle(e => e.ChartId == cashAccount && e.Debit == 300, $"{country}: a client line puts the money on the cash desk side");
        byClient.Entries.Single(e => e.Credit == 300).ChartId.Should().Be(byCash.Entries.Single(e => e.Credit == 500).ChartId, $"{country}: the clients' account");

        var byBank = await PostAsync("Payment", (bank.Id, "Bank"), (supplier.Id, "Supplier"), "BANK_ACCOUNT", (bank.Id, bank.TypeId), 200);
        byBank.Entries.Should().ContainSingle(e => e.ChartId == bankAccount && e.Credit == 200, $"{country}: the money out of the account the bank holds");
        byBank.Entries.Should().ContainSingle(e => e.ChartId != bankAccount && e.Debit == 200, $"{country}: to the supplier");

        var reversal = await documents.ReverseAsync(byCash.Id, null, date, ct);
        var reversed = await context.Set<ChartValue>().Where(v => v.DocumentId == reversal.Id).ToListAsync(ct);
        reversed.Select(e => (e.ChartId, e.Debit, e.Credit)).Should().BeEquivalentTo(byCash.Entries.Select(e => (e.ChartId, e.Credit, e.Debit)),
            $"{country}: the receipt's reversal mirrors it");

        var offer = types.Single(t => t.Name.In("en") == "Sales offer, goods");
        offer.NeverPosted.Should().BeTrue();
        var offerCategory = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == offer.Id).Select(l => l.CategoryId).FirstAsync(ct);
        var kept = await documents.SaveAsync(new DocumentDto
        {
            TypeId = offer.Id, CategoryIds = [offerCategory], DocumentDate = date, PostingDate = date, FromId = cash.Id, FromName = "Cash desk", ToId = client.Id, ToName = "Client",
        }, ct);
        var post = async () => await documents.PostAsync(kept.Id!.Value, ct);
        (await post.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain(ErrorCodes.NeverPostedError, $"{country}: an offer is kept, never posted");
    }

    [Theory]
    [MemberData(nameof(Countries))]
    public async Task A_countrys_pack_posts_a_point_of_sale_bill_of_goods_and_services_and_its_reversal_mirrors_it(string country)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "A company", country, [("cost_center_type:POS_RESTAURANT", "POS", "Till")]);
        OrganisationIds company;
        Guid user;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            db.Add(staff);
            await db.SaveChangesAsync(ct);
            var owner = new User { TypeId = staff.Id, Code = "OWNER", Name = TranslatedText.English("Owner"), TenantId = tenantId };
            db.Add(owner);
            await db.SaveChangesAsync(ct);
            user = owner.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var client = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id, Code = "C1", Name = TranslatedText.English("Client"),
            TenantId = tenantId, OwnerId = company.Corporate };
        context.Add(client);
        async Task<Product> ProductAsync(string category, string code)
        {
            var c = await context.Set<ProductCategory>().SingleAsync(x => x.Code == category, ct);
            var type = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == c.Id).Select(l => l.TypeId).FirstAsync(ct);
            var product = new Product { TypeId = type, Code = code, Name = TranslatedText.English(code), TenantId = tenantId, OwnerId = company.Corporate };
            context.AddRange(product, new InterProductCategory { ElementId = product.Id, CategoryId = c.Id, TenantId = tenantId, OwnerId = company.Corporate });
            return product;
        }

        var beer = await ProductAsync("PRODUCT", "BEER");
        var service = await ProductAsync("SERVICE", "SERVICE");
        await context.SaveChangesAsync(ct);
        var till = await context.Set<CostCenter>().SingleAsync(c => c.Code == "POS", ct);
        var unit = (await context.Set<Unit>().FirstAsync(ct)).Id;
        var type = (await context.Set<DocumentType>().ToListAsync(ct)).Single(t => t.Name.In("en") == "Point-of-sale receipt");
        var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == type.Id).Select(l => l.CategoryId).FirstAsync(ct);
        var vat = await context.Set<VatGroup>().OrderByDescending(v => v.Rate).FirstAsync(ct); // a type with no VAT groups of its own takes any
        var kinds = await context.Set<TransactionType>().ToDictionaryAsync(t => t.Code!, t => t.Id, ct);
        TransactionDto Line(string kind, Product product, decimal price) => new()
        {
            TypeId = kinds[kind], ElementId = product.Id, ElementTypeId = product.TypeId, ElementName = product.Code, UnitId = unit, Quantity = 1, Price = price,
            VatGroupId = vat.Id, FromId = till.Id, FromName = "Till",
        };

        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var date = new DateOnly(2026, 10, 30);
        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = type.Id, CategoryIds = [category], DocumentDate = date, PostingDate = date, ToId = client.Id, ToName = "Client",
            Transactions = [Line("ITEM", beer, 100), Line("SERVICE_ITEM", service, 50)],
        }, ct);
        await documents.PostAsync(saved.Id!.Value, ct);
        var entries = await context.Set<ChartValue>().Where(v => v.DocumentId == saved.Id).ToListAsync(ct);
        var gross = 150 * (1 + vat.Rate / 100);
        (entries.Sum(e => e.Debit), entries.Sum(e => e.Credit)).Should().Be((gross, gross), $"{country}: the bill balances");
        entries.Where(e => e.Debit > 0).Select(e => e.ChartId).Distinct().Should().ContainSingle($"{country}: the clients' account takes the whole bill");
        entries.Where(e => e.Credit == 100).Should().ContainSingle($"{country}: the goods' revenue");
        entries.Where(e => e.Credit == 50).Should().ContainSingle($"{country}: the services' revenue");

        var reversal = await documents.ReverseAsync(saved.Id!.Value, null, date, ct);
        var reversed = await context.Set<ChartValue>().Where(v => v.DocumentId == reversal.Id).ToListAsync(ct);
        reversed.Select(e => (e.ChartId, e.Debit, e.Credit)).Should().BeEquivalentTo(entries.Select(e => (e.ChartId, e.Credit, e.Debit)),
            $"{country}: the point-of-sale reversal mirrors the bill");
    }

    [Theory]
    [MemberData(nameof(Countries))]
    public async Task A_countrys_pack_posts_invoices_of_services_sold_and_bought_and_a_reversal_mirrors_them(string country)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "A company", country, [("cost_center_type:OFFICE", "OFF", "Office")]);
        OrganisationIds company;
        Guid user;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            db.Add(staff);
            await db.SaveChangesAsync(ct);
            var owner = new User { TypeId = staff.Id, Code = "OWNER", Name = TranslatedText.English("Owner"), TenantId = tenantId };
            db.Add(owner);
            await db.SaveChangesAsync(ct);
            user = owner.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var ltd = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id;
        var client = new Partner { TypeId = ltd, Code = "C1", Name = TranslatedText.English("Client"), TenantId = tenantId, OwnerId = company.Corporate };
        var supplier = new Partner { TypeId = ltd, Code = "S1", Name = TranslatedText.English("Supplier"), TenantId = tenantId, OwnerId = company.Corporate };
        var serviceCategory = await context.Set<ProductCategory>().SingleAsync(x => x.Code == "SERVICE", ct);
        var serviceType = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == serviceCategory.Id).Select(l => l.TypeId).FirstAsync(ct);
        var service = new Product { TypeId = serviceType, Code = "CONSULTING", Name = TranslatedText.English("Consulting"), TenantId = tenantId, OwnerId = company.Corporate };
        context.AddRange(client, supplier, service, new InterProductCategory { ElementId = service.Id, CategoryId = serviceCategory.Id, TenantId = tenantId, OwnerId = company.Corporate });
        await context.SaveChangesAsync(ct);
        var office = await context.Set<CostCenter>().SingleAsync(c => c.Code == "OFF", ct);
        var unit = (await context.Set<Unit>().FirstAsync(ct)).Id;
        var vat = await context.Set<VatGroup>().OrderByDescending(v => v.Rate).FirstAsync(ct);
        var serviceLine = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "SERVICE_ITEM", ct)).Id;
        var types = await context.Set<DocumentType>().ToListAsync(ct);
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var date = new DateOnly(2026, 10, 30);

        async Task<(Guid Id, List<ChartValue> Entries)> PostAsync(string typeName, (Guid Id, string Name) from, (Guid Id, string Name) to, decimal price)
        {
            var type = types.Single(t => t.Name.In("en") == typeName);
            var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == type.Id).Select(l => l.CategoryId).FirstAsync(ct);
            var saved = await documents.SaveAsync(new DocumentDto
            {
                TypeId = type.Id, CategoryIds = [category], DocumentDate = date, PostingDate = date, FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name,
                Transactions = [new TransactionDto { TypeId = serviceLine, ElementId = service.Id, ElementTypeId = service.TypeId, ElementName = "Consulting", UnitId = unit,
                    Quantity = 1, Price = price, VatGroupId = vat.Id }],
            }, ct);
            await documents.PostAsync(saved.Id!.Value, ct);
            return (saved.Id!.Value, await context.Set<ChartValue>().Where(v => v.DocumentId == saved.Id).ToListAsync(ct));
        }

        var sold = await PostAsync("Sales invoice, services", (office.Id, "Office"), (client.Id, "Client"), 1000);
        var gross = 1000 * (1 + vat.Rate / 100);
        (sold.Entries.Sum(e => e.Debit), sold.Entries.Sum(e => e.Credit)).Should().Be((gross, gross), $"{country}: the sale balances");
        sold.Entries.Should().ContainSingle(e => e.Credit == 1000, $"{country}: the services' revenue").And.ContainSingle(e => e.Debit == gross, $"{country}: the client");

        var bought = await PostAsync("Purchase invoice, services", (supplier.Id, "Supplier"), (office.Id, "Office"), 400);
        var cost = 400 * (1 + vat.Rate / 100);
        (bought.Entries.Sum(e => e.Debit), bought.Entries.Sum(e => e.Credit)).Should().Be((cost, cost), $"{country}: the purchase balances");
        bought.Entries.Should().ContainSingle(e => e.Debit == 400, $"{country}: the services' cost").And.ContainSingle(e => e.Credit == cost, $"{country}: the supplier");
        bought.Entries.Single(e => e.Debit == 400).ChartId!.Value.Should().NotBe(sold.Entries.Single(e => e.Credit == 1000).ChartId!.Value);

        var reversal = await documents.ReverseAsync(sold.Id, null, date, ct);
        var reversed = await context.Set<ChartValue>().Where(v => v.DocumentId == reversal.Id).ToListAsync(ct);
        reversed.Select(e => (e.ChartId, e.Debit, e.Credit)).Should().BeEquivalentTo(sold.Entries.Select(e => (e.ChartId, e.Credit, e.Debit)),
            $"{country}: the services sale's reversal mirrors it");
    }

    [Theory]
    [MemberData(nameof(Countries))]
    public async Task A_countrys_pack_posts_fixed_assets_bought_on_the_account_of_each_assets_category(string country)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "A company", country, [("cost_center_type:OFFICE", "OFF", "Office")]);
        OrganisationIds company;
        Guid user;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            db.Add(staff);
            await db.SaveChangesAsync(ct);
            var owner = new User { TypeId = staff.Id, Code = "OWNER", Name = TranslatedText.English("Owner"), TenantId = tenantId };
            db.Add(owner);
            await db.SaveChangesAsync(ct);
            user = owner.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var supplier = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id, Code = "S1", Name = TranslatedText.English("Supplier"),
            TenantId = tenantId, OwnerId = company.Corporate };
        context.Add(supplier);
        var assetCategory = await context.Set<ProductCategory>().SingleAsync(x => x.Code == "ASSET", ct);
        var assetType = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == assetCategory.Id).Select(l => l.TypeId).FirstAsync(ct);
        async Task<Product> AssetAsync(string category, string code)
        {
            var c = await context.Set<ProductCategory>().SingleAsync(x => x.Code == category, ct);
            var product = new Product { TypeId = assetType, Code = code, Name = TranslatedText.English(code), TenantId = tenantId, OwnerId = company.Corporate };
            context.AddRange(product, new InterProductCategory { ElementId = product.Id, CategoryId = c.Id, TenantId = tenantId, OwnerId = company.Corporate });
            return product;
        }

        var shop = await AssetAsync("BUILDINGS", "SHOP");
        var van = await AssetAsync("VEHICLES", "VAN");
        await context.SaveChangesAsync(ct);
        var office = await context.Set<CostCenter>().SingleAsync(c => c.Code == "OFF", ct);
        var unit = (await context.Set<Unit>().FirstAsync(ct)).Id;
        var vat = await context.Set<VatGroup>().OrderByDescending(v => v.Rate).FirstAsync(ct);
        var assetLine = (await context.Set<TransactionType>().SingleAsync(t => t.Code == "FIXED_ASSET", ct)).Id;
        var type = (await context.Set<DocumentType>().ToListAsync(ct)).Single(t => t.Name.In("en") == "Purchase invoice, goods");
        var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == type.Id).Select(l => l.CategoryId).FirstAsync(ct);
        TransactionDto Line(Product asset, decimal price) => new()
        {
            TypeId = assetLine, ElementId = asset.Id, ElementTypeId = asset.TypeId, ElementName = asset.Code, UnitId = unit, Quantity = 1, Price = price, VatGroupId = vat.Id,
        };

        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var date = new DateOnly(2026, 10, 30);
        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = type.Id, CategoryIds = [category], DocumentDate = date, PostingDate = date, FromId = supplier.Id, FromName = "Supplier", ToId = office.Id, ToName = "Office",
            Transactions = [Line(shop, 50000), Line(van, 12345)],
        }, ct);
        await documents.PostAsync(saved.Id!.Value, ct);
        var entries = await context.Set<ChartValue>().Where(v => v.DocumentId == saved.Id).ToListAsync(ct);
        var gross = 62345 * (1 + vat.Rate / 100);
        (entries.Sum(e => e.Debit), entries.Sum(e => e.Credit)).Should().Be((gross, gross), $"{country}: the purchase balances");
        var building = entries.Single(e => e.Debit == 50000).ChartId;
        var vehicle = entries.Single(e => e.Debit == 12345).ChartId;
        building!.Value.Should().NotBe(vehicle!.Value, $"{country}: a building and a vehicle go to their own categories' accounts");
        entries.Where(e => e.Credit > 0).Select(e => e.ChartId).Distinct().Should().ContainSingle($"{country}: the supplier");
    }

    [Theory]
    [MemberData(nameof(Countries))]
    public async Task A_countrys_pack_posts_customs_on_the_goods_imported_and_a_transfer_on_both_warehouses(string country)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "A company", country, [("cost_center_type:WAREHOUSE", "WH1", "Warehouse 1"), ("cost_center_type:WAREHOUSE", "WH2", "Warehouse 2")]);
        OrganisationIds company;
        Guid user;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = tenantId };
            db.Add(staff);
            await db.SaveChangesAsync(ct);
            var owner = new User { TypeId = staff.Id, Code = "OWNER", Name = TranslatedText.English("Owner"), TenantId = tenantId };
            db.Add(owner);
            await db.SaveChangesAsync(ct);
            user = owner.Id;
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, user);
        var request = new RequestContext(tenantId, user, company.Corporate, new HashSet<Guid>(), IsAdministrator: true);
        await using var context = Context(tenant);
        var customs = new Partner { TypeId = (await context.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id, Code = "CUS", Name = TranslatedText.English("Customs"),
            TenantId = tenantId, OwnerId = company.Corporate };
        context.Add(customs);
        var goods = await context.Set<ProductCategory>().SingleAsync(x => x.Code == "PRODUCT", ct);
        var goodsType = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == goods.Id).Select(l => l.TypeId).FirstAsync(ct);
        var wine = new Product { TypeId = goodsType, Code = "WINE", Name = TranslatedText.English("Wine"), TenantId = tenantId, OwnerId = company.Corporate };
        context.AddRange(wine, new InterProductCategory { ElementId = wine.Id, CategoryId = goods.Id, TenantId = tenantId, OwnerId = company.Corporate });
        await context.SaveChangesAsync(ct);
        var (first, second) = (await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH1", ct), await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH2", ct));
        var unit = (await context.Set<Unit>().FirstAsync(ct)).Id;
        var vat = await context.Set<VatGroup>().OrderByDescending(v => v.Rate).FirstAsync(ct);
        var zero = await context.Set<VatGroup>().OrderBy(v => v.Rate).FirstAsync(ct);
        var kinds = await context.Set<TransactionType>().ToDictionaryAsync(t => t.Code!, t => t.Id, ct);
        var types = await context.Set<DocumentType>().ToListAsync(ct);
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var date = new DateOnly(2026, 10, 30);

        async Task<List<ChartValue>> PostAsync(string typeName, (Guid Id, string Name) from, (Guid Id, string Name) to, string kind, decimal price, Guid vatGroup)
        {
            var type = types.Single(t => t.Name.In("en") == typeName);
            var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == type.Id).Select(l => l.CategoryId).FirstAsync(ct);
            var saved = await documents.SaveAsync(new DocumentDto
            {
                TypeId = type.Id, CategoryIds = [category], DocumentDate = date, PostingDate = date, FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name,
                Transactions = [new TransactionDto { TypeId = kinds[kind], ElementId = wine.Id, ElementTypeId = wine.TypeId, ElementName = "Wine", UnitId = unit, Quantity = 1,
                    Price = price, VatGroupId = vatGroup }],
            }, ct);
            await documents.PostAsync(saved.Id!.Value, ct);
            return await context.Set<ChartValue>().Where(v => v.DocumentId == saved.Id).ToListAsync(ct);
        }

        var duty = await PostAsync("Customs declaration", (customs.Id, "Customs"), (first.Id, "Warehouse 1"), "CUSTOMS", 300, vat.Id);
        var withVat = 300 * (1 + vat.Rate / 100);
        (duty.Sum(e => e.Debit), duty.Sum(e => e.Credit)).Should().Be((withVat, withVat), $"{country}: the customs declaration balances");
        duty.Should().ContainSingle(e => e.Debit == 300 && e.CostCenterId == first.Id, $"{country}: the duty goes onto the goods, at the warehouse");

        var moved = await PostAsync("Goods transfer note", (first.Id, "Warehouse 1"), (second.Id, "Warehouse 2"), "ITEM", 80, zero.Id);
        moved.Should().HaveCount(2);
        moved.Select(e => e.ChartId).Distinct().Should().ContainSingle($"{country}: a transfer keeps the goods on their account");
        moved.Should().ContainSingle(e => e.Debit == 80 && e.CostCenterId == second.Id).And.ContainSingle(e => e.Credit == 80 && e.CostCenterId == first.Id);
    }

    [Theory]
    [MemberData(nameof(Countries))]
    public async Task Every_reversal_type_of_a_countrys_pack_reverses_exactly_one_type_of_its_own(string country)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("CO", "A company", country, null);
        OrganisationIds company;
        await using (var db = Context(new TenantContext()))
        {
            company = Organisation.CreateCompany(db, tenantId, setup);
            Organisation.SetUpCompany(db, company, setup);
            await db.SaveChangesAsync(ct);
        }

        await using var context = Context(new TenantContext());
        var types = await context.Set<DocumentType>().IgnoreQueryFilters().Where(t => t.OwnerId == company.Corporate && t.Active).ToListAsync(ct);
        var byId = types.ToDictionary(t => t.Id);
        var reversalCategory = await context.Set<DocumentCategory>().IgnoreQueryFilters().SingleAsync(c => c.OwnerId == company.Corporate && c.Code == "REVERSALS", ct);
        var inReversals = await context.Set<InterDocumentTypeCategory>().IgnoreQueryFilters().Where(l => l.CategoryId == reversalCategory.Id && l.Active).Select(l => l.TypeId).ToListAsync(ct);

        var reversals = types.Where(t => t.IsReversal).ToList();
        reversals.Should().HaveCount(17, $"{country}: every reversal type of the base");
        reversals.Should().OnlyContain(t => t.ReversedDocumentTypeId != null && byId.ContainsKey(t.ReversedDocumentTypeId.Value) && !byId[t.ReversedDocumentTypeId.Value].IsReversal,
            $"{country}: a reversal type reverses one type of the pack that is not a reversal type itself");
        reversals.Select(t => t.ReversedDocumentTypeId).Should().OnlyHaveUniqueItems($"{country}: one reversal type per type");
        types.Where(t => !t.IsReversal).Should().OnlyContain(t => t.ReversedDocumentTypeId == null);
        inReversals.Should().OnlyContain(id => byId[id].IsReversal, $"{country}: every type in Reversals is a reversal type");
        var posReturn = types.Single(t => t.Name.In("en") == "Point-of-sale return");
        byId[posReturn.ReversedDocumentTypeId!.Value].Name.In("en").Should().Be("Point-of-sale receipt");
        byId[types.Single(t => t.Name.In("en") == "Point-of-sale refund").ReversedDocumentTypeId!.Value].Name.In("en").Should().Be("Point-of-sale receipt of money");
    }
}

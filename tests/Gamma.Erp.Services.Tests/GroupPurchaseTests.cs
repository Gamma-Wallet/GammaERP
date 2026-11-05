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
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>A sale from the group is the company's purchase (the founder, 2026-10-31): a draft in the company's own books, its products found by code.</summary>
public sealed class GroupPurchaseTests : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 10, 31);
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
    public async Task A_group_sale_makes_the_companys_draft_found_by_code_and_a_reversal_takes_it_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        // The company is in another country than the group: its document types have other codes, its rows are its own.
        var setup = new CompanySetup("ROMA", "Roma", "IT", [("cost_center_type:WAREHOUSE", "WH-R", "Magazzino")]);
        OrganisationIds group;
        await using (var db = Context(new TenantContext()))
        {
            group = Organisation.Create(db, tenantId, "Grupi", "AL", [setup]);
            Organisation.SetUpCompany(db, group, setup);
            Organisation.SetUpGroup(db, group, "AL");
            await db.SaveChangesAsync(ct);
            await Organisation.LinkGroupPartnersAsync(db, group, ct);
            await db.SaveChangesAsync(ct);
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, null);
        await using var context = Context(tenant);
        var (corporate, roma) = (group.Corporate, group.Companies["ROMA"]);
        var request = new RequestContext(tenantId, null, corporate, new HashSet<Guid>(), IsAdministrator: true);
        var atRoma = request with { NodeId = roma };
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var theirs = new DocumentService(context, atRoma, new Visibility(context, atRoma), TimeProvider.System);

        var warehouse = await context.Set<CostCenter>().SingleAsync(c => c.Code == "WH-R", ct);
        var romaGoods = await context.Set<GroupPurchaseSetting>().SingleAsync(s => s.OwnerId == roma && s.Code == "GOODS", ct);
        romaGoods.WarehouseId = warehouse.Id;
        Product Make(Guid owner, Guid type, string code, string name) => new() { TypeId = type, Code = code, Name = TranslatedText.English(name), TenantId = tenantId, OwnerId = owner };
        async Task<(Guid Category, Guid Type)> GoodsAsync(Guid owner)
        {
            var category = await context.Set<ProductCategory>().SingleAsync(c => c.OwnerId == owner && c.Code == "PRODUCT", ct);
            return (category.Id, await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == category.Id).Select(l => l.TypeId).FirstAsync(ct));
        }

        var (groupCategory, groupType) = await GoodsAsync(corporate);
        var (romaCategory, romaType) = await GoodsAsync(roma);
        var (oil, salt, romaOil) = (Make(corporate, groupType, "OIL", "Vaj"), Make(corporate, groupType, "SALT", "Kripë"), Make(roma, romaType, "OIL", "Olio"));
        context.AddRange(oil, salt, romaOil, new InterProductCategory { ElementId = oil.Id, CategoryId = groupCategory, TenantId = tenantId, OwnerId = corporate },
            new InterProductCategory { ElementId = salt.Id, CategoryId = groupCategory, TenantId = tenantId, OwnerId = corporate },
            new InterProductCategory { ElementId = romaOil.Id, CategoryId = romaCategory, TenantId = tenantId, OwnerId = roma });
        await context.SaveChangesAsync(ct);

        var client = await context.Set<Partner>().SingleAsync(p => p.OwnerId == corporate && p.GroupMemberId == roma, ct);
        var supplier = await context.Set<Partner>().SingleAsync(p => p.OwnerId == roma && p.GroupMemberId == corporate, ct);
        var saleType = await context.Set<GroupPurchaseSetting>().Where(s => s.OwnerId == corporate && s.Code == "GOODS").Select(s => s.SaleTypeId!.Value).SingleAsync(ct);
        var saleCategory = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == saleType).Select(l => l.CategoryId).FirstAsync(ct);
        var item = await context.Set<TransactionType>().Where(t => t.OwnerId == corporate && t.Code == "ITEM").Select(t => t.Id).SingleAsync(ct);
        var unit = await context.Set<Unit>().Where(u => u.OwnerId == corporate && u.Code == "PIECE").Select(u => u.Id).SingleAsync(ct);
        var vat = await context.Set<VatGroup>().Where(v => v.OwnerId == corporate && v.Rate == 20).Select(v => v.Id).FirstAsync(ct);
        var store = await StockedAsync(context, documents, tenantId, corporate, [(oil, 10), (salt, 4)], ct);
        var sale = await documents.SaveAsync(new DocumentDto
        {
            TypeId = saleType, CategoryIds = [saleCategory], PostingDate = Day, FromId = store, ToId = client.Id, ToName = "Roma",
            Transactions = [new TransactionDto { TypeId = item, ElementId = oil.Id, ElementName = "Vaj", UnitId = unit, Quantity = 10, Price = 5, VatGroupId = vat },
                new TransactionDto { TypeId = item, ElementId = salt.Id, ElementName = "Kripë", UnitId = unit, Quantity = 4, Price = 1, VatGroupId = vat }],
        }, ct);
        (await context.Set<Document>().CountAsync(d => d.GroupSaleId == sale.Id, ct)).Should().Be(0, "a sale makes the purchase when it is posted");
        await documents.PostAsync(sale.Id!.Value, ct);

        var made = await context.Set<Document>().SingleAsync(d => d.GroupSaleId == sale.Id, ct);
        var draft = (await theirs.DocumentAsync(made.Id, ct))!;
        (draft.Posted, draft.CompanyId, draft.FromId, draft.ToId, draft.ExternalNumber).Should().Be((false, roma, supplier.Id, warehouse.Id, sale.Code));
        (await context.Set<DocumentType>().SingleAsync(t => t.Id == draft.TypeId, ct)).OwnerId.Should().Be(roma);
        draft.Transactions.Should().HaveCount(2);
        var (first, second) = (draft.Transactions[0], draft.Transactions[1]);
        (first.ElementId, first.ElementName, first.Quantity, first.Price).Should().Be((romaOil.Id, "Olio", 10m, 5m), "the company's own product of the same code");
        (await context.Set<Unit>().SingleAsync(u => u.Id == first.UnitId, ct)).OwnerId.Should().Be(roma);
        first.VatGroupId.Should().BeNull("Italy has no 20%: the company chooses its own VAT group");
        (second.ElementId, second.ElementName).Should().Be((null, "Kripë"), "the company has no SALT: the line is left for it to choose");

        // The group never sees it, and the company never sees the sale.
        (await documents.DocumentAsync(made.Id, ct)).Should().BeNull();
        (await theirs.DocumentAsync(sale.Id!.Value, ct)).Should().BeNull();

        // It can't be posted with a line left open; the company chooses a product of its own and it goes.
        var open = async () => await theirs.PostAsync(made.Id, ct);
        (await open.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("transactions[1].element");
        var romaSalt = Make(roma, romaType, "SALE", "Sale");
        context.AddRange(romaSalt, new InterProductCategory { ElementId = romaSalt.Id, CategoryId = romaCategory, TenantId = tenantId, OwnerId = roma });
        await context.SaveChangesAsync(ct);
        first.ExchangeRate.Should().BeNull("the group sells in lek, Roma keeps euro and has no rate for the day: its to set");
        var romaVat = await context.Set<VatGroup>().Where(v => v.OwnerId == roma && v.Rate == 22).Select(v => v.Id).SingleAsync(ct);
        var filled = await theirs.SaveAsync(draft with { Transactions = [first with { ExchangeRate = 0.01m, VatGroupId = romaVat }, second with { ElementId = romaSalt.Id,
            ElementName = "Sale", ElementTypeId = romaType, TypeConfId = first.TypeConfId, UnitId = first.UnitId, VatGroupId = romaVat, ExchangeRate = 0.01m }] }, ct);

        // A part of the sale reversed: the draft now holds what is left. All of it reversed: no draft.
        var soldLine = (await documents.DocumentAsync(sale.Id!.Value, ct))!.Transactions[0];
        var lastDraft = async () => await context.Set<Document>().Where(d => d.GroupSaleId == sale.Id).ToListAsync(ct);
        await documents.ReverseAsync(sale.Id!.Value, new Dictionary<Guid, decimal> { [soldLine.Id!.Value] = 3 }, Day, ct);
        var remade = (await lastDraft()).Should().ContainSingle().Which;
        remade.Id.Should().NotBe(filled.Id!.Value);
        (await theirs.DocumentAsync(remade.Id, ct))!.Transactions[0].Quantity.Should().Be(7);

        await documents.ReverseAsync(sale.Id!.Value, null, Day, ct);
        (await lastDraft()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_sale_to_anyone_else_makes_nothing_and_a_company_without_the_row_takes_no_part()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.CreateVersion7();
        var setup = new CompanySetup("TIRANA", "Tirana", "AL");
        OrganisationIds group;
        await using (var db = Context(new TenantContext()))
        {
            group = Organisation.Create(db, tenantId, "Grupi", "AL", [setup]);
            Organisation.SetUpCompany(db, group, setup);
            Organisation.SetUpGroup(db, group, "AL");
            await db.SaveChangesAsync(ct);
            await Organisation.LinkGroupPartnersAsync(db, group, ct);
            await db.SaveChangesAsync(ct);
        }

        var tenant = new TenantContext();
        tenant.Set(tenantId, null);
        await using var context = Context(tenant);
        var (corporate, tirana) = (group.Corporate, group.Companies["TIRANA"]);
        var request = new RequestContext(tenantId, null, corporate, new HashSet<Guid>(), IsAdministrator: true);
        var documents = new DocumentService(context, request, new Visibility(context, request), TimeProvider.System);
        var client = await context.Set<Partner>().SingleAsync(p => p.OwnerId == corporate && p.GroupMemberId == tirana, ct);
        var outsider = new Partner { TypeId = client.TypeId, Code = "OUT", Name = TranslatedText.English("Klient"), TenantId = tenantId, OwnerId = corporate };
        context.Add(outsider);
        foreach (var row in await context.Set<GroupPurchaseSetting>().Where(s => s.OwnerId == tirana).ToListAsync(ct))
        {
            row.Active = false;
        }

        await context.SaveChangesAsync(ct);
        var goods = await context.Set<ProductCategory>().SingleAsync(c => c.OwnerId == corporate && c.Code == "PRODUCT", ct);
        var goodsType = await context.Set<InterProductTypeCategory>().Where(l => l.CategoryId == goods.Id).Select(l => l.TypeId).FirstAsync(ct);
        var oil = new Product { TypeId = goodsType, Code = "OIL", Name = TranslatedText.English("Vaj"), TenantId = tenantId, OwnerId = corporate };
        context.AddRange(oil, new InterProductCategory { ElementId = oil.Id, CategoryId = goods.Id, TenantId = tenantId, OwnerId = corporate });
        await context.SaveChangesAsync(ct);
        var saleType = await context.Set<GroupPurchaseSetting>().Where(s => s.OwnerId == corporate && s.Code == "GOODS").Select(s => s.SaleTypeId!.Value).SingleAsync(ct);
        var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == saleType).Select(l => l.CategoryId).FirstAsync(ct);
        var item = await context.Set<TransactionType>().Where(t => t.OwnerId == corporate && t.Code == "ITEM").Select(t => t.Id).SingleAsync(ct);
        var unit = await context.Set<Unit>().Where(u => u.OwnerId == corporate && u.Code == "PIECE").Select(u => u.Id).SingleAsync(ct);
        var vat = await context.Set<VatGroup>().Where(v => v.OwnerId == corporate && v.Rate == 20).Select(v => v.Id).FirstAsync(ct);
        var store = await StockedAsync(context, documents, tenantId, corporate, [(oil, 2)], ct);
        // A group with books behaves as a company: its own warehouse goes directly under it, in the register.
        var register = new EntityService<CostCenterCategory, CostCenterType, InterCostCenterTypeCategory, CostCenterTypeInfo, CostCenterTypeInfoChoice, CostCenter, CostCenterInfo,
            InterCostCenterCategory, CostCenterAccess>(context, request, new Visibility(context, request));
        var warehouseType = await context.Set<CostCenterType>().SingleAsync(t => t.OwnerId == corporate && t.Code == "WAREHOUSE", ct);
        var warehouseCategory = await context.Set<InterCostCenterTypeCategory>().Where(l => l.TypeId == warehouseType.Id).Select(l => l.CategoryId).FirstAsync(ct);
        (await register.SaveElementAsync(new Gamma.Erp.Models.Base.Dto.ElementDto { TypeId = warehouseType.Id, IdSup = corporate, Code = "WH-2", Name = new() { ["en"] = "Magazina 2" },
            CategoryIds = [warehouseCategory] }, ct)).IdSup.Should().Be(corporate);
        // Where the pack says "the company" (a journal's place), the group is that in its own books.
        (await context.Set<Gamma.Erp.Models.Platform.Elements>().CountAsync(e => e.OwnerId == corporate && e.TypeId == group.CompanyType, ct)).Should().Be(0);
        (await register.LookupAsync(group.CompanyCategory, group.CorporateType, null, "en", 1, 50, ct)).Items.Select(i => i.Id).Should().Contain(corporate);
        foreach (var to in new[] { outsider.Id, client.Id })
        {
            var sale = await documents.SaveAsync(new DocumentDto
            {
                TypeId = saleType, CategoryIds = [category], PostingDate = Day, FromId = store, ToId = to, ToName = "Klient",
                Transactions = [new TransactionDto { TypeId = item, ElementId = oil.Id, ElementName = "Vaj", UnitId = unit, Quantity = 1, Price = 100, VatGroupId = vat }],
            }, ct);
            await documents.PostAsync(sale.Id!.Value, ct);
            (await context.Set<Document>().CountAsync(d => d.GroupSaleId == sale.Id, ct)).Should().Be(0);
        }
    }

    /// <summary>A warehouse of the group with these products bought into it, from an outside supplier.</summary>
    private static async Task<Guid> StockedAsync(ErpDbContext context, DocumentService documents, Guid tenantId, Guid corporate, (Product Product, decimal Quantity)[] goods, CancellationToken ct)
    {
        var type = await context.Set<CostCenterType>().Where(t => t.OwnerId == corporate && t.Code == "WAREHOUSE").Select(t => t.Id).SingleAsync(ct);
        var store = new CostCenter { TypeId = type, IdSup = corporate, Code = "WH-G", Name = TranslatedText.English("Magazina"), TenantId = tenantId, OwnerId = corporate };
        var partnerType = await context.Set<PartnerType>().Where(t => t.OwnerId == corporate && t.Code == "SME").Select(t => t.Id).SingleAsync(ct);
        var seller = new Partner { TypeId = partnerType, Code = "S1", Name = TranslatedText.English("Furnitori"), TenantId = tenantId, OwnerId = corporate };
        context.AddRange(store, seller);
        await context.SaveChangesAsync(ct);
        var purchase = await context.Set<GroupPurchaseSetting>().Where(s => s.OwnerId == corporate && s.Code == "GOODS").Select(s => s.PurchaseTypeId!.Value).SingleAsync(ct);
        var category = await context.Set<InterDocumentTypeCategory>().Where(l => l.TypeId == purchase).Select(l => l.CategoryId).FirstAsync(ct);
        var item = await context.Set<TransactionType>().Where(t => t.OwnerId == corporate && t.Code == "ITEM").Select(t => t.Id).SingleAsync(ct);
        var unit = await context.Set<Unit>().Where(u => u.OwnerId == corporate && u.Code == "PIECE").Select(u => u.Id).SingleAsync(ct);
        var vat = await context.Set<VatGroup>().Where(v => v.OwnerId == corporate && v.Rate == 20).Select(v => v.Id).FirstAsync(ct);
        await documents.PostAsync((await documents.SaveAsync(new DocumentDto
        {
            TypeId = purchase, CategoryIds = [category], PostingDate = Day, FromId = seller.Id, ToId = store.Id,
            Transactions = [.. goods.Select(g => new TransactionDto { TypeId = item, ElementId = g.Product.Id, ElementName = g.Product.Name.In("en"), UnitId = unit,
                Quantity = g.Quantity, Price = 1, VatGroupId = vat })],
        }, ct)).Id!.Value, ct);
        return store.Id;
    }
}

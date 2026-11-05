using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Base.Dto;
using Gamma.Erp.Models.Charts;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Posting;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Persistence.Tenancy;
using Gamma.Erp.Seed;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

using Products = Gamma.Erp.Services.Entities.EntityService<Gamma.Erp.Models.Products.ProductCategory, Gamma.Erp.Models.Products.ProductType,
    Gamma.Erp.Models.Products.InterProductTypeCategory, Gamma.Erp.Models.Products.ProductTypeInfo, Gamma.Erp.Models.Products.ProductTypeInfoChoice,
    Gamma.Erp.Models.Products.Product, Gamma.Erp.Models.Products.ProductInfo, Gamma.Erp.Models.Products.InterProductCategory, Gamma.Erp.Models.Products.ProductAccess>;
using Partners = Gamma.Erp.Services.Entities.EntityService<Gamma.Erp.Models.Partners.PartnerCategory, Gamma.Erp.Models.Partners.PartnerType,
    Gamma.Erp.Models.Partners.InterPartnerTypeCategory, Gamma.Erp.Models.Partners.PartnerTypeInfo, Gamma.Erp.Models.Partners.PartnerTypeInfoChoice,
    Gamma.Erp.Models.Partners.Partner, Gamma.Erp.Models.Partners.PartnerInfo, Gamma.Erp.Models.Partners.InterPartnerCategory, Gamma.Erp.Models.Partners.PartnerAccess>;

// The example corporate: Gamma Group Inc. (US) with a restaurant in Italy, a fast food in Albania and a supermarket in Cyprus; only the
// Albanian company has a starter pack so far. Then, for Fast Food Tirana, what a day of work leaves: two users, the accounts its cash desk
// and bank hold, two partners, and a sale, a receipt and a payment, posted. Runs on an empty database (after the migrations) and refuses
// to run twice.
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Gamma.Erp.DevSeed \"<connection string>\"");
    return 1;
}

var tenant = new TenantContext();
ErpDbContext ContextOf(TenantContext of) => new((DbContextOptions<ErpDbContext>)PersistenceExtensions.Configure(new DbContextOptionsBuilder<ErpDbContext>(), args[0])
    .AddInterceptors(new TenantConnectionInterceptor(of), new HistoryInterceptor(of, TimeProvider.System)).Options);
ErpDbContext Context() => ContextOf(tenant);
var ct = CancellationToken.None;

OrganisationIds organisation;
Guid admin;
await using (var db = Context())
{
    await db.Database.MigrateAsync(ct);
    if (await db.Set<CostCenter>().AnyAsync(ct))
    {
        Console.Error.WriteLine("The database already has an organisation.");
        return 1;
    }

    CompanySetup[] companies =
    [
        new("ROMA", "Ristorante Roma", "IT", Activity: "RESTAURANT"),
        new("TIRANA", "Fast Food Tirana", "AL", [("cost_center_type:BASE", "BASE", "Base"), ("cost_center_type:CASH_DESK", "CASH", "Cash desk"), ("cost_center_type:BANK", "BANK", "Bank"),
            ("cost_center_type:POINT_OF_SALE", "POS1", "Point of sale Blloku"), ("cost_center_type:WAREHOUSE", "WH1", "Warehouse")], "FAST_FOOD"),
        new("LIMASSOL", "Supermarket Limassol", "CY", Activity: "SUPERMARKET"),
    ];
    organisation = Organisation.Create(db, Guid.CreateVersion7(), "Gamma Group Inc.", "US", companies);
    // Tirana first: the seed finds Tirana's rows by their codes, so the other companies' packs come after its day (Roma below).
    var rows = Organisation.SetUpCompany(db, organisation, companies.Single(c => c.Code == "TIRANA"));
    await db.SaveChangesAsync(ct);
    Console.WriteLine($"Corporate {organisation.TenantId}: 3 companies; Fast Food Tirana got {rows} rows of the Albanian pack and its cost centres.");

    // The users (users are elements too; every row records who wrote it).
    var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = organisation.TenantId };
    db.Add(staff);
    await db.SaveChangesAsync(ct);
    // Sign-in names and passwords: the password comes from GAMMA_ERP_DEV_PASSWORD (never in the repository) and only its hash is kept.
    var password = Environment.GetEnvironmentVariable("GAMMA_ERP_DEV_PASSWORD");
    var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<User>();
    var hash = string.IsNullOrWhiteSpace(password) ? null : hasher.HashPassword(null!, password);
    var adminUser = new User { TypeId = staff.Id, Code = "ADMIN", Name = TranslatedText.English("Administrator"), Username = "admin", PasswordHash = hash, TenantId = organisation.TenantId };
    var seller = new User { TypeId = staff.Id, Code = "SELLER", Name = TranslatedText.English("Seller"), Username = "seller", PasswordHash = hash, TenantId = organisation.TenantId };
    db.AddRange(adminUser, seller);
    await db.SaveChangesAsync(ct);
    admin = adminUser.Id;

    // Their roles (user categories; administrators marked so) and the cost centres they work at.
    var administrators = new UserCategory { Code = "ADMINISTRATORS", Name = TranslatedText.English("Administrators"), IsElement = true, IsAdministrator = true, TenantId = organisation.TenantId };
    var sellers = new UserCategory { Code = "SELLERS", Name = TranslatedText.English("Sellers"), IsElement = true, TenantId = organisation.TenantId };
    db.AddRange(administrators, sellers);
    await db.SaveChangesAsync(ct);
    db.AddRange(new InterUserCategory { ElementId = adminUser.Id, CategoryId = administrators.Id, TenantId = organisation.TenantId },
        new InterUserCategory { ElementId = seller.Id, CategoryId = sellers.Id, TenantId = organisation.TenantId });
    var pointOfSale = await db.Set<CostCenter>().SingleAsync(c => c.Code == "POS1", ct);
    db.AddRange(new InterUserCostCenter { UserId = adminUser.Id, CostCenterId = organisation.Companies["TIRANA"], TenantId = organisation.TenantId },
        new InterUserCostCenter { UserId = seller.Id, CostCenterId = pointOfSale.Id, TenantId = organisation.TenantId });
    await db.SaveChangesAsync(ct);
    Console.WriteLine($"Users: admin {adminUser.Id} (Administrators, at Fast Food Tirana), seller {seller.Id} (Sellers, at Point of sale Blloku); " +
        $"passwords {(hash is null ? "NOT set (GAMMA_ERP_DEV_PASSWORD is empty)" : "set from GAMMA_ERP_DEV_PASSWORD, kept as hashes")}.");
}

// From here on everything is written as the administrator, at Fast Food Tirana, through the services.
tenant.Set(organisation.TenantId, admin);
var tirana = organisation.Companies["TIRANA"];
var request = new RequestContext(organisation.TenantId, admin, tirana, new HashSet<Guid>(), IsAdministrator: true);
await using (var db = Context())
{
    // The accounts the cash desk and the bank hold (their "Account" property), as the guided start will ask.
    var charts = await db.Set<Chart>().ToListAsync(ct);
    var cashAccount = charts.Single(c => c.Code == "5311"); // cash in hand, lek
    var bankAccount = charts.Single(c => c.Code == "5121"); // cash at bank, lek
    var chartLookups = db.Set<Elements>().Where(e => e.Table == "tbl_chart").Select(e => (Guid?)e.Id);
    foreach (var (code, account) in new[] { ("CASH", cashAccount), ("BANK", bankAccount) })
    {
        var costCenter = await db.Set<CostCenter>().SingleAsync(c => c.Code == code, ct);
        var property = await db.Set<CostCenterTypeInfo>().SingleAsync(p => p.TypeId == costCenter.TypeId && p.Kind == PropertyKind.Lookup && chartLookups.Contains(p.ElementsId), ct);
        db.Add(new CostCenterInfo { ElementId = costCenter.Id, TypeInfoId = property.Id, Value = JsonValue.Create(account.Id.ToString()), OwnerId = tirana });
    }

    // Every cost centre in its type's category, and the values the pack proposes for what is still empty (as the sign-up does).
    await db.SaveChangesAsync(ct);
    await Organisation.CompleteCostCentersAsync(db, tirana, ct);

    // Fast Food Tirana's own setting: a document stays held by the user editing it for 30 minutes without being renewed.
    var holdTimeout = (await db.Set<CostCenterTypeInfo>().Where(p => p.Kind == PropertyKind.Number).ToListAsync(ct))
        .Single(p => p.Name.In(TranslatedText.DefaultLanguage) == DocumentService.HoldTimeoutProperty);
    db.Add(new CostCenterInfo { ElementId = tirana, TypeInfoId = holdTimeout.Id, Value = JsonValue.Create(30m), OwnerId = tirana });
    // Its stock settings: its warehouses never go negative, its partners' stock may (a supplier's, for example).
    var numberSettings = await (from p in db.Set<CostCenterTypeInfo>() join e in db.Set<Elements>() on p.ElementsId equals e.Id
                                where p.Kind == PropertyKind.Number select new { Property = p, e.Table }).ToListAsync(ct);
    foreach (var (table, allowed) in new[] { ("tbl_cost_center", 0m), ("tbl_partner", 1m) })
    {
        var setting = numberSettings.Single(x => x.Table == table && x.Property.Name.In(TranslatedText.DefaultLanguage) == Gamma.Erp.Services.Posting.MovementService.NegativeStockProperty);
        db.Add(new CostCenterInfo { ElementId = tirana, TypeInfoId = setting.Property.Id, Value = JsonValue.Create(allowed), OwnerId = tirana });
    }
    await db.SaveChangesAsync(ct);

    // The group's own chart, at Gamma Group and shared by its companies: one account per class of the Albanian chart (equity,
    // non-current assets, inventories, third parties, cash, expenses, income, off-balance). Fast Food Tirana maps every Albanian account to
    // the one of its class, so each posting is written again there, with the company and its country (AL).
    var group = organisation.Corporate;
    var groupChart = new ChartCategory { Code = "GROUP", Name = TranslatedText.English("Gamma Group chart"), IsElement = true, IsStatutory = false, OwnerId = group };
    var groupType = new ChartType { Code = "GROUP", Name = TranslatedText.English("Group account"), IsElement = true, OwnerId = group };
    db.AddRange(groupChart, groupType, new InterChartTypeCategory { TypeId = groupType.Id, CategoryId = groupChart.Id, OwnerId = group });
    var albanian = charts.Where(c => c.Active && c.OwnerId == tirana).ToList();
    var byId = albanian.ToDictionary(c => c.Id);
    Chart Top(Chart c) => c.IdSup is { } up && byId.TryGetValue(up, out var parent) ? Top(parent) : c;
    var groupAccounts = albanian.Where(c => c.IdSup is null).ToDictionary(c => c.Id, c => new Chart
    {
        TypeId = groupType.Id, Code = $"G-{c.Code}", Name = TranslatedText.English(c.Name.In(TranslatedText.DefaultLanguage) ?? c.Code!), OwnerId = group,
    });
    db.AddRange(groupAccounts.Values);
    db.AddRange(groupAccounts.Values.Select(a => new InterChartCategory { ElementId = a.Id, CategoryId = groupChart.Id, OwnerId = group }));
    var leaves = albanian.Where(c => !albanian.Any(k => k.IdSup == c.Id)).ToList();
    db.AddRange(leaves.Select(a => new InterChartChart { Chart1Id = a.Id, Chart2Id = groupAccounts[Top(a).Id].Id, OwnerId = tirana }));
    var mapped = leaves.Count;

    // Its ownership: Gamma Group owns all of Fast Food Tirana (a subsidiary) and 30 % of Supermarket Limassol (an associate). The group
    // chart holds the companies' entries at 100 %; the share applies at consolidation.
    var ownership = (await db.Set<CostCenterTypeInfo>().Where(p => p.Kind == PropertyKind.Number).ToListAsync(ct))
        .Single(p => p.Name.In(TranslatedText.DefaultLanguage) == Organisation.OwnershipProperty);
    var method = (await db.Set<CostCenterTypeInfo>().Where(p => p.Kind == PropertyKind.List).ToListAsync(ct))
        .Single(p => p.Name.In(TranslatedText.DefaultLanguage) == Organisation.ConsolidationProperty);
    var methods = await db.Set<CostCenterTypeInfoChoice>().Where(c => c.TypeInfoId == method.Id).ToListAsync(ct);
    Guid Method(string name) => methods.Single(c => c.Name.In(TranslatedText.DefaultLanguage)!.StartsWith(name, StringComparison.Ordinal)).Id;
    var limassol = organisation.Companies["LIMASSOL"];
    foreach (var (company, share, how) in new[] { (tirana, 100m, "Full"), (limassol, 30m, "Equity") })
    {
        db.Add(new CostCenterInfo { ElementId = company, TypeInfoId = ownership.Id, Value = JsonValue.Create(share), OwnerId = company });
        db.Add(new CostCenterInfo { ElementId = company, TypeInfoId = method.Id, Value = JsonValue.Create(Method(how).ToString()), OwnerId = company });
    }

    await db.SaveChangesAsync(ct);
    Console.WriteLine($"Group chart at Gamma Group: {groupAccounts.Count} accounts; Fast Food Tirana maps its {mapped} accounts to it by class. " +
        "Ownership: Tirana 100 % (full), Limassol 30 % (equity method).");

    // Two partners: a client and a supplier.
    var visibility = new Visibility(db, request);
    var partners = new Partners(db, request, visibility);
    var clients = (await db.Set<PartnerCategory>().SingleAsync(c => c.Code == "CLIENTS", ct)).Id;
    var suppliers = (await db.Set<PartnerCategory>().SingleAsync(c => c.Code == "SUPPLIERS", ct)).Id;
    var sme = (await db.Set<PartnerType>().SingleAsync(t => t.Code == "SME", ct)).Id;
    var ltd = (await db.Set<PartnerType>().SingleAsync(t => t.Code == "LTD", ct)).Id;
    var client = await partners.SaveElementAsync(new ElementDto { Name = new() { ["en"] = "Bar Lura" }, TypeId = sme, CategoryIds = [clients] }, ct);
    var supplier = await partners.SaveElementAsync(new ElementDto { Name = new() { ["en"] = "Fresh Food sh.p.k." }, TypeId = ltd, CategoryIds = [suppliers] }, ct);
    Console.WriteLine($"Partners: client Bar Lura {client.Id}, supplier Fresh Food sh.p.k. {supplier.Id}.");

    // A sale, a receipt (cash) and a payment (bank), each with one account line (GLT), then posted.
    var documents = new DocumentService(db, request, visibility, TimeProvider.System);
    var gl = (await db.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT", ct)).Id;
    var vat20 = (await db.Set<Gamma.Erp.Models.Payments.VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id;
    var vat0 = (await db.Set<Gamma.Erp.Models.Payments.VatGroup>().SingleAsync(v => v.Rate == 0, ct)).Id;
    var warehouse = await db.Set<CostCenter>().SingleAsync(c => c.Code == "WH1", ct);
    var cash = await db.Set<CostCenter>().SingleAsync(c => c.Code == "CASH", ct);
    var bank = await db.Set<CostCenter>().SingleAsync(c => c.Code == "BANK", ct);
    var date = new DateOnly(2026, 10, 29);

    // The line's own account (the GLT element) is a real account, never a group: revenue for the sale, the client's receivable for the
    // receipt, the supplier's payable for the payment.
    async Task<(Guid Type, Guid Category, Chart Account)> TypeAsync(string typeCode, string categoryCode, string accountCode)
    {
        var category = await db.Set<DocumentCategory>().SingleAsync(c => c.Code == categoryCode, ct);
        var inCategory = db.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == category.Id).Select(l => l.TypeId);
        var type = await db.Set<DocumentType>().SingleAsync(t => t.Code == typeCode && inCategory.Contains(t.Id), ct);
        return (type.Id, category.Id, charts.Single(c => c.Code == accountCode));
    }

    async Task PostAsync(string what, string typeCode, string categoryCode, string accountCode, (Guid Id, string Name) from, (Guid Id, string Name) to, decimal? price, Guid? vat,
        decimal? withVat = null)
    {
        var (type, category, account) = await TypeAsync(typeCode, categoryCode, accountCode);
        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = type, CategoryIds = [category], DocumentDate = date, PostingDate = date, FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name,
            Transactions = [new TransactionDto { TypeId = gl, ElementId = account.Id, ElementTypeId = account.TypeId, ElementName = what, Price = price, ValueVat = withVat, VatGroupId = vat }],
        }, ct);
        var posted = await documents.PostAsync(saved.Id!.Value, ct);
        Console.WriteLine($"{what}: {posted.Code}, posted.");
    }

    await PostAsync("Sale to Bar Lura", "FSHM", "SALES", "705", (warehouse.Id, "Warehouse"), (client.Id!.Value, "Bar Lura"), 1000, vat20);
    await PostAsync("Bar Lura pays in cash", "MA", "CASH_DESK", "411", (client.Id!.Value, "Bar Lura"), (cash.Id, "Cash desk"), 1200, vat0);
    await PostAsync("Sale to Bar Lura, typed with VAT", "FSHM", "SALES", "705", (warehouse.Id, "Warehouse"), (client.Id!.Value, "Bar Lura"), null, vat20, withVat: 2400);
    // One invoice, two warehouses: line 2 takes its merchandise from the point of sale, not from the document's warehouse.
    var pos = await db.Set<CostCenter>().SingleAsync(c => c.Code == "POS1", ct);
    var (saleType, saleCategory, revenue) = await TypeAsync("FSHM", "SALES", "705");
    var twoWarehouses = await documents.SaveAsync(new DocumentDto
    {
        TypeId = saleType, CategoryIds = [saleCategory], DocumentDate = date, PostingDate = date, FromId = warehouse.Id, FromName = "Warehouse",
        ToId = client.Id, ToName = "Bar Lura",
        Transactions =
        [
            new TransactionDto { TypeId = gl, ElementId = revenue.Id, ElementTypeId = revenue.TypeId, ElementName = "Burgers from the warehouse", Price = 300, VatGroupId = vat20 },
            new TransactionDto { TypeId = gl, ElementId = revenue.Id, ElementTypeId = revenue.TypeId, ElementName = "Drinks from the point of sale", Price = 100, VatGroupId = vat20,
                FromId = pos.Id, FromName = "Point of sale Blloku" },
        ],
    }, ct);
    Console.WriteLine($"Sale from two warehouses: {(await documents.PostAsync(twoWarehouses.Id!.Value, ct)).Code}, posted.");
    await PostAsync("Payment to Fresh Food by bank", "MP", "BANK", "401", (bank.Id, "Bank"), (supplier.Id!.Value, "Fresh Food sh.p.k."), 500, vat0);

    // Goods: a product bought into two places and sold out of one, then a sale the warehouse can't cover.
    var products = new Products(db, request, visibility);
    var goods = (await db.Set<Gamma.Erp.Models.Products.ProductCategory>().SingleAsync(c => c.Code == "PRODUCT", ct)).Id;
    var goodsType = (await db.Set<Gamma.Erp.Models.Products.ProductType>().SingleAsync(t => t.Code == "GOODS", ct)).Id;
    var piece = (await db.Set<Gamma.Erp.Models.Units.Unit>().SingleAsync(u => u.Code == "PIECE", ct)).Id;
    var burger = await products.SaveElementAsync(new ElementDto { Name = new() { ["en"] = "Burger" }, TypeId = goodsType, CategoryIds = [goods] }, ct);
    var itemType = (await db.Set<TransactionType>().SingleAsync(t => t.Code == "ITEM", ct)).Id;

    async Task<Guid> TypeOfAsync(string typeCode, string categoryCode)
    {
        var category = await db.Set<DocumentCategory>().SingleAsync(c => c.Code == categoryCode, ct);
        var inCategory = db.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == category.Id).Select(l => l.TypeId);
        return (await db.Set<DocumentType>().SingleAsync(t => t.Code == typeCode && inCategory.Contains(t.Id), ct)).Id;
    }

    async Task<string> GoodsAsync(string typeCode, string categoryCode, (Guid Id, string Name) from, (Guid Id, string Name) to, decimal quantity, decimal price)
    {
        var category = (await db.Set<DocumentCategory>().SingleAsync(c => c.Code == categoryCode, ct)).Id;
        var saved = await documents.SaveAsync(new DocumentDto
        {
            TypeId = await TypeOfAsync(typeCode, categoryCode), CategoryIds = [category], DocumentDate = date, PostingDate = date,
            FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name,
            Transactions = [new TransactionDto { TypeId = itemType, ElementId = burger.Id, ElementName = "Burger", UnitId = piece, Quantity = quantity, Price = price, VatGroupId = vat20 }],
        }, ct);
        return (await documents.PostAsync(saved.Id!.Value, ct)).Code!;
    }

    Console.WriteLine($"Purchase of 100 burgers into the warehouse: {await GoodsAsync("FBM", "PURCHASES", (supplier.Id!.Value, "Fresh Food sh.p.k."), (warehouse.Id, "Warehouse"), 100, 2)}, posted.");
    Console.WriteLine($"Purchase of 100 burgers into the point of sale: {await GoodsAsync("FBM", "PURCHASES", (supplier.Id!.Value, "Fresh Food sh.p.k."), (pos.Id, "Point of sale Blloku"), 100, 2)}, posted.");
    var burgerSale = await GoodsAsync("FSHM", "SALES", (warehouse.Id, "Warehouse"), (client.Id!.Value, "Bar Lura"), 30, 5);
    Console.WriteLine($"Sale of 30 burgers from the warehouse: {burgerSale}, posted.");
    // Depreciation: an oven registered in both books, two monthly runs; the company book's runs go into posted journals.
    var assets = (await db.Set<Gamma.Erp.Models.Products.ProductCategory>().SingleAsync(c => c.Code == "ASSET", ct)).Id;
    var assetType = (await db.Set<Gamma.Erp.Models.Products.ProductType>().SingleAsync(t => t.Code == "ASSET", ct)).Id;
    var oven = await products.SaveElementAsync(new ElementDto { Name = new() { ["en"] = "Oven" }, TypeId = assetType, CategoryIds = [assets] }, ct);
    var books = await db.Set<Gamma.Erp.Models.Depreciation.DepreciationBook>().ToListAsync(ct);
    var companyBook = books.Single(b => b.Posts);
    var taxBook = books.Single(b => !b.Posts);
    var depreciation = new Gamma.Erp.Services.Depreciation.DepreciationService(db, request, documents);
    await depreciation.RegisterAsync(new(oven.Id!.Value, piece, companyBook.Id, 12000, new DateOnly(2026, 1, 1), 60), ct);
    // The tax book: the life and rate its scenario proposes for the asset's category (Albania: 20 % a year, straight-line, other assets).
    await depreciation.RegisterAsync(new(oven.Id!.Value, piece, taxBook.Id, 12000, new DateOnly(2026, 1, 1)), ct);
    var journalType = await TypeOfAsync("FAA", "JOURNAL");
    var journalCategory = (await db.Set<DocumentCategory>().SingleAsync(c => c.Code == "JOURNAL", ct)).Id;
    foreach (var runDate in new[] { new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28) })
    {
        var company = (await depreciation.RunAsync(companyBook.Id, runDate, ct)).Single();
        var tax = (await depreciation.RunAsync(taxBook.Id, runDate, ct)).Single();
        var journal = await depreciation.JournalAsync(companyBook.Id, runDate, journalType, journalCategory, ct);
        Console.WriteLine($"Depreciation {runDate:yyyy-MM-dd}: company book {company.Amount} ({company.Days} days, book value {company.BookValueAfter}), journal {journal.Code} posted {journal.Posted}; " +
            $"tax book {tax.Amount} (book value {tax.BookValueAfter}).");
    }

    Console.WriteLine($"Oven's remaining value: company book {await depreciation.RemainingValueAsync(oven.Id!.Value, piece, companyBook.Id, ct)}, " +
        $"tax book {await depreciation.RemainingValueAsync(oven.Id!.Value, piece, taxBook.Id, ct)}.");

    try
    {
        await GoodsAsync("FSHM", "SALES", (warehouse.Id, "Warehouse"), (client.Id!.Value, "Bar Lura"), 80, 5);
        Console.WriteLine("Sale of 80 more burgers from the warehouse: posted (it should not have been).");
    }
    catch (ValidationException refused)
    {
        Console.WriteLine($"Sale of 80 more burgers from the warehouse: refused. {refused.Message}");
    }

    // Reversal: a posted document is corrected by its return (from and to swapped), partly here; never more than is left.
    var sold = await db.Set<Document>().SingleAsync(d => d.Code == burgerSale, ct);
    var soldLine = await db.Set<Transaction>().SingleAsync(t => t.DocumentId == sold.Id, ct);
    var returned = await documents.ReverseAsync(sold.Id, new Dictionary<Guid, decimal> { [soldLine.Id] = 10 }, date, ct);
    Console.WriteLine($"Return of 10 of the 30 burgers of {burgerSale}: {returned.Code}, posted {returned.Posted}, from {returned.FromName} to {returned.ToName}.");
    foreach (var (what, attempt) in new (string, Func<Task>)[]
    {
        ("Return of 25 more burgers", () => documents.ReverseAsync(sold.Id, new Dictionary<Guid, decimal> { [soldLine.Id] = 25 }, date, ct)),
    })
    {
        try
        {
            await attempt();
            Console.WriteLine($"{what}: done (it should not have been).");
        }
        catch (ValidationException refused)
        {
            Console.WriteLine($"{what}: refused. {refused.Message}");
        }
    }

    // A receipt reversed whole: its own reversal type (KMA, in Returns), cash and receivable back.
    var receipt = await db.Set<Document>().FirstAsync(d => d.Code!.StartsWith("MA"), ct);
    var reversedReceipt = await documents.ReverseAsync(receipt.Id, null, date, ct);
    Console.WriteLine($"Reversal of the cash receipt {receipt.Code}: {reversedReceipt.Code}, posted {reversedReceipt.Posted}, from {reversedReceipt.FromName} to {reversedReceipt.ToName}.");

    // What the CRM statistics show for the two partners, and the ranking of the sales category.
    var statistics = new Gamma.Erp.Services.Partners.PartnerStatisticsService(db, visibility);
    foreach (var partner in new[] { client, supplier })
    {
        var figures = (await statistics.StatisticsAsync(partner.Id!.Value, null, null, ct))!;
        Console.WriteLine($"{figures.Name}: balance {figures.Balance}, documents {figures.FirstDocument}..{figures.LastDocument}");
        foreach (var c in figures.Categories)
        {
            Console.WriteLine($"   {c.Category}: {c.Documents} documents, value {c.Value}, VAT {c.Vat}, with VAT {c.ValueVat}, debit {c.Debit}, credit {c.Credit}");
        }

        foreach (var product in figures.TopProducts)
        {
            Console.WriteLine($"   product {product.Product}: {product.Quantity} for {product.Value}");
        }

        foreach (var row in await statistics.LedgerAsync(partner.Id!.Value, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), ct))
        {
            Console.WriteLine($"   ledger {row.PostingDate} {row.Code ?? "(before)"} [{row.Category}] debit {row.Debit} credit {row.Credit} balance {row.Balance}");
        }
    }

    var salesCategory = (await db.Set<DocumentCategory>().SingleAsync(c => c.Code == "SALES", ct)).Id;
    foreach (var rank in (await statistics.RankingAsync(salesCategory, null, null, 1, 10, ct)).Items)
    {
        Console.WriteLine($"Sales ranking: {rank.Name}, {rank.Documents} documents, debit {rank.Debit}, credit {rank.Credit}, balance {rank.Balance}");
    }
}

// The group's other companies, each with its country's pack from the catalogue, a cash desk holding its account, a client and its day: a
// sale of goods and the client paying in cash, posted. Ristorante Roma (Italy, Restaurant → Hospitality) and Supermarket Limassol (Cyprus,
// Supermarket → Retail).
async Task CompanyDayAsync(string code, string name, string country, string activity, string cashAccount, string client, string sale, string revenue,
    decimal rate, string receipt, string receivable, decimal price)
{
    var company = organisation.Companies[code];
    await using (var db = Context())
    {
        var rows = Organisation.SetUpCompany(db, organisation, new CompanySetup(code, name, country,
            [("cost_center_type:CASH_DESK", $"{code}-CASH", "Cash desk"), ("cost_center_type:WAREHOUSE", $"{code}-WH", "Warehouse")], activity, FromCatalogue: true));
        await db.SaveChangesAsync(ct);
        var cash = await db.Set<CostCenter>().SingleAsync(c => c.Code == $"{code}-CASH", ct);
        var chartLookups = db.Set<Elements>().Where(e => e.Table == "tbl_chart" && e.OwnerId == company).Select(e => (Guid?)e.Id);
        var property = await db.Set<CostCenterTypeInfo>().SingleAsync(p => p.TypeId == cash.TypeId && p.Kind == PropertyKind.Lookup && chartLookups.Contains(p.ElementsId), ct);
        var account = await db.Set<Chart>().SingleAsync(c => c.Code == cashAccount && c.OwnerId == company, ct);
        db.Add(new CostCenterInfo { ElementId = cash.Id, TypeInfoId = property.Id, Value = JsonValue.Create(account.Id.ToString()), OwnerId = company });
        await db.SaveChangesAsync(ct);
        await Organisation.CompleteCostCentersAsync(db, company, ct);
        await db.SaveChangesAsync(ct);
        Console.WriteLine($"{name}: {rows} rows of the {country} pack ({activity} from the catalogue), its cash desk on {cashAccount}.");
    }

    var at = request with { NodeId = company };
    await using (var db = Context())
    {
        var visibility = new Visibility(db, at);
        var partners = new Partners(db, at, visibility);
        var documents = new DocumentService(db, at, visibility, TimeProvider.System);
        var clients = (await db.Set<PartnerCategory>().SingleAsync(c => c.Code == "CLIENTS" && c.OwnerId == company, ct)).Id;
        var ltd = (await db.Set<PartnerType>().SingleAsync(t => t.Code == "LTD" && t.OwnerId == company, ct)).Id;
        var partner = await partners.SaveElementAsync(new ElementDto { Name = new() { ["en"] = client }, TypeId = ltd, CategoryIds = [clients] }, ct);
        var cash = await db.Set<CostCenter>().SingleAsync(c => c.Code == $"{code}-CASH", ct);
        var warehouse = await db.Set<CostCenter>().SingleAsync(c => c.Code == $"{code}-WH", ct);
        var gl = (await db.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT" && t.OwnerId == company, ct)).Id;
        var date = new DateOnly(2026, 10, 29);

        async Task<DocumentDto> PostAsync(string typeCode, string categoryCode, string accountCode, decimal vatRate, (Guid Id, string Name) from, (Guid Id, string Name) to,
            decimal amount, string what)
        {
            var category = await db.Set<DocumentCategory>().SingleAsync(c => c.Code == categoryCode && c.OwnerId == company, ct);
            var inCategory = db.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == category.Id).Select(l => l.TypeId);
            var type = await db.Set<DocumentType>().SingleAsync(t => t.Code == typeCode && t.OwnerId == company && inCategory.Contains(t.Id), ct);
            var line = await db.Set<Chart>().SingleAsync(c => c.Code == accountCode && c.OwnerId == company, ct);
            var vat = await db.Set<Gamma.Erp.Models.Payments.VatGroup>().SingleAsync(v => v.Rate == vatRate && v.OwnerId == company, ct);
            var saved = await documents.SaveAsync(new DocumentDto
            {
                TypeId = type.Id, CategoryIds = [category.Id], DocumentDate = date, PostingDate = date, FromId = from.Id, FromName = from.Name, ToId = to.Id, ToName = to.Name,
                Transactions = [new TransactionDto { TypeId = gl, ElementId = line.Id, ElementTypeId = line.TypeId, ElementName = what, Price = amount, VatGroupId = vat.Id }],
            }, ct);
            return await documents.PostAsync(saved.Id!.Value, ct);
        }

        var sold = await PostAsync(sale, "SALES", revenue, rate, (warehouse.Id, "Warehouse"), (partner.Id!.Value, client), price, "Goods");
        var paid = await PostAsync(receipt, "CASH_DESK", receivable, 0, (partner.Id!.Value, client), (cash.Id, "Cash desk"), price * (1 + rate / 100), "Payment");
        Console.WriteLine($"{name}: sale {sold.Code} and receipt {paid.Code}, posted.");
    }
}

await CompanyDayAsync("ROMA", "Ristorante Roma", "IT", "RESTAURANT", "08.202", "Studio Bianchi S.r.l.", "FVM", "30.010", 10, "INC", "05.000", 500);
await CompanyDayAsync("LIMASSOL", "Supermarket Limassol", "CY", "SUPERMARKET", "2801", "Kafeneio Lemesos Ltd", "INV", "5001", 19, "REC", "2201", 400);

// A second customer, on its own: a barber shop that is its own root (no corporate above it), with the Albanian pack, a user and a sale.
var barberTenant = Guid.CreateVersion7();
// It takes its activity's configuration from the catalogue (Barber → Beauty): service sales, the POS and cash, no warehouse.
var barberSetup = new CompanySetup("ARTI", "Berberia Arti", "AL", [("cost_center_type:CASH_DESK", "CASH", "Cash desk")], "BARBER", FromCatalogue: true);
// Its own tenant context: a context never changes tenant once set (each customer is isolated).
var barberContext = new TenantContext();
OrganisationIds barberShop;
Guid barber;
await using (var db = ContextOf(new TenantContext()))
{
    barberShop = Organisation.CreateCompany(db, barberTenant, barberSetup);
    Organisation.SetUpCompany(db, barberShop, barberSetup);
    await db.SaveChangesAsync(ct);
    await Organisation.CompleteCostCentersAsync(db, barberShop.Corporate, ct);
    await db.SaveChangesAsync(ct);
    var staff = new UserType { Code = "STAFF", Name = TranslatedText.English("Staff"), IsElement = true, TenantId = barberTenant };
    var owners = new UserCategory { Code = "ADMINISTRATORS", Name = TranslatedText.English("Administrators"), IsElement = true, IsAdministrator = true, TenantId = barberTenant };
    db.AddRange(staff, owners);
    await db.SaveChangesAsync(ct);
    var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<User>();
    var password = Environment.GetEnvironmentVariable("GAMMA_ERP_DEV_PASSWORD");
    var user = new User { TypeId = staff.Id, Code = "BARBER", Name = TranslatedText.English("Barber"), Username = "barber",
        PasswordHash = string.IsNullOrWhiteSpace(password) ? null : hasher.HashPassword(null!, password), TenantId = barberTenant };
    db.Add(user);
    await db.SaveChangesAsync(ct);
    db.AddRange(new InterUserCategory { ElementId = user.Id, CategoryId = owners.Id, TenantId = barberTenant },
        new InterUserCostCenter { UserId = user.Id, CostCenterId = barberShop.Corporate, TenantId = barberTenant });
    await db.SaveChangesAsync(ct);
    barber = user.Id;
}

barberContext.Set(barberTenant, barber);
var barberRequest = new RequestContext(barberTenant, barber, barberShop.Corporate, new HashSet<Guid>(), IsAdministrator: true);
await using (var db = ContextOf(barberContext))
{
    var visibility = new Visibility(db, barberRequest);
    Console.WriteLine($"Berberia Arti: its own root, and its company is {((await visibility.CompanyAsync(ct)) == barberShop.Corporate ? "itself" : "something else (wrong)")}.");
    var partners = new Partners(db, barberRequest, visibility);
    var clients = (await db.Set<PartnerCategory>().SingleAsync(c => c.Code == "CLIENTS", ct)).Id;
    var walkIn = await partners.SaveElementAsync(new ElementDto { Name = new() { ["en"] = "Walk-in client" }, TypeId = (await db.Set<PartnerType>().SingleAsync(t => t.Code == "UNKNOWN", ct)).Id,
        CategoryIds = [clients] }, ct);
    var documents = new DocumentService(db, barberRequest, visibility, TimeProvider.System);
    var sales = await db.Set<DocumentCategory>().SingleAsync(c => c.Code == "SALES", ct);
    var inSales = db.Set<InterDocumentTypeCategory>().Where(l => l.CategoryId == sales.Id).Select(l => l.TypeId);
    var saleType = await db.Set<DocumentType>().SingleAsync(t => t.Code == "FSHSH" && inSales.Contains(t.Id), ct);
    var gl = (await db.Set<TransactionType>().SingleAsync(t => t.Code == "ACCOUNT", ct)).Id;
    var services = await db.Set<Chart>().SingleAsync(c => c.Code == "704", ct); // sales of works and services
    var cashDesk = await db.Set<CostCenter>().SingleAsync(c => c.Code == "CASH", ct);
    var haircut = await documents.SaveAsync(new DocumentDto
    {
        TypeId = saleType.Id, CategoryIds = [sales.Id], DocumentDate = new DateOnly(2026, 10, 29), PostingDate = new DateOnly(2026, 10, 29),
        FromId = cashDesk.Id, FromName = "Cash desk", ToId = walkIn.Id, ToName = "Walk-in client",
        Transactions = [new TransactionDto { TypeId = gl, ElementId = services.Id, ElementTypeId = services.TypeId, ElementName = "Haircut", Price = 800,
            VatGroupId = (await db.Set<Gamma.Erp.Models.Payments.VatGroup>().SingleAsync(v => v.Rate == 20, ct)).Id }],
    }, ct);
    Console.WriteLine($"Berberia Arti's sale: {(await documents.PostAsync(haircut.Id!.Value, ct)).Code}, posted.");
}

return 0;

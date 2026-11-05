using System.Text.Json.Nodes;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Pos;

public sealed record MenuProductDto(Guid ProductId, string? Code, string? Name, Guid? CategoryId, decimal? Price, Guid? CurrencyId);

/// <summary>A product by its barcode, with its price, for the shop screen.</summary>
public sealed record PosBarcodeDto(string Barcode, Guid ProductId, string? Code, string? Name, decimal? Price, Guid? CurrencyId);

public sealed record MenuCategoryDto(Guid CategoryId, string? Name, IReadOnlyList<MenuProductDto> Products);

public sealed record SaleMenuDto(Guid Id, string? Code, string? Name, TimeOnly? StartsAt, TimeOnly? EndsAt, IReadOnlyList<MenuCategoryDto> Categories);

/// <summary>A bill not yet paid in full: its total, what was paid and reversed, and what is still open (company currency).</summary>
public sealed record UnpaidBillDto(Guid Id, string? Code, Guid? TableId, string? Table, Guid? ClientId, string? Client, Guid? CreatedBy, DateTimeOffset CreatedOn,
    decimal Total, decimal Paid, decimal Reversed, decimal Open);

/// <summary>
/// The settings a point of sale works with (B13): its own row's columns, each empty one taken from the company's default row; with the names
/// to show and the columns still empty in both (what an administrator has to set before the point of sale can sell or take money).
/// </summary>
/// <summary>A product's price at the till: in the company's currency at today's rates.</summary>
public sealed record PosPrice(decimal Value, Guid? CurrencyId);

public sealed record PosSettingsDto(Guid? PointOfSaleId, Guid? BillTypeId, string? BillType, Guid? MoneyInTypeId, string? MoneyInType, Guid? MoneyOutTypeId,
    string? MoneyOutType, Guid? CashDeskId, string? CashDesk, Guid? WalkInClientId, string? WalkInClient, Guid? PaymentMethodId, string? PaymentMethod,
    IReadOnlyList<string> Missing, string? Screen = null, Guid? ProductId = null, bool RequiresShift = true, Guid? OrderTypeId = null, string? OrderType = null,
    string? Product = null, string? PointOfSale = null, Guid? TillId = null);

/// <summary>A property of a place (its type's property and the value it holds), e.g. a table's position or seats.</summary>
public sealed record PosPropertyDto(Guid Id, string? Name, JsonNode? Value);

/// <summary>Who wrote open bills at a place.</summary>
public sealed record PosWaiterDto(Guid Id, string? Name);

/// <summary>
/// A place of the point of sale's floor plan (B14): a cost centre below it; one with places below it groups them (a hall), one without is
/// where bills are written (a table, a counter); with its properties and its bills still open, their total and who wrote them.
/// </summary>
public sealed record PosPlaceDto(Guid Id, Guid? ParentId, string? Code, string? Name, bool HoldsPlaces, IReadOnlyList<PosPropertyDto> Properties, int OpenBills,
    decimal Open, IReadOnlyList<PosWaiterDto> Waiters, Guid? TypeId = null);

/// <summary>
/// Which bills to pay (the given ones, a table's, or every open one of the bill type), with which money document type, into which cash desk
/// (its line is the cash desk), how, and on which day.
/// </summary>
public sealed record PayBillsRequest(Guid BillTypeId, Guid PaymentTypeId, Guid CashDeskId, IReadOnlyList<Guid>? BillIds = null, Guid? TableId = null, bool All = false,
    Guid? PaymentMethodId = null, DateOnly? Date = null, Guid? PointOfSaleId = null);

/// <summary>The payment methods a cost centre offers: those linked to it; null when it has no link (then every method).</summary>
public sealed record PaymentMethodsAtDto(IReadOnlyList<Guid>? Ids);

/// <summary>
/// The point of sale's backend: the sale menus open at a time with their categories, products and prices; the bills not yet paid
/// (per waiter, per table); paying one, a table's or all of them with one money document per client, linked to the bills it pays and posted.
/// Which document types are bills and payments is the caller's (from the configuration), never named in code.
/// </summary>
public sealed class PosService(ErpDbContext db, Visibility visibility, DocumentService documents, TimeProvider clock)
{
    /// <summary>The product property (on the product types) that holds its barcode (the founder, 2026-10-31: barcodes are properties of products).</summary>
    /// <summary>The code of the product property that holds the barcode (configuration: the property carries it, whatever its name).</summary>
    public const string BarcodeCode = "BARCODE";

    /// <summary>The menus open at a time (by default now) at a point of sale (or those for every point of sale), each with its products and prices.</summary>
    public async Task<IReadOnlyList<SaleMenuDto>> CurrentMenusAsync(Guid? pointOfSale, TimeOnly? at, string language, CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var time = at ?? TimeOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var menus = (await db.Set<SaleMenu>().AsNoTracking().Where(m => m.PointOfSaleId == null || m.PointOfSaleId == pointOfSale).ToListAsync(ct).ConfigureAwait(false))
            .Where(m => (m.OwnerId is null || nodes.Contains(m.OwnerId.Value)) && Open(m, time)).OrderBy(m => m.SortOrder).ToList();
        var menuIds = menus.Select(m => m.Id).ToList();
        var links = await db.Set<SaleMenuCategory>().AsNoTracking().Where(l => menuIds.Contains(l.SaleMenuId)).OrderBy(l => l.SortOrder).ToListAsync(ct).ConfigureAwait(false);
        var categoryIds = links.Select(l => l.ProductCategoryId).Distinct().ToList();
        var categories = await db.Set<ProductCategory>().AsNoTracking().Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var inCategory = await (from l in db.Set<InterProductCategory>().AsNoTracking() join p in db.Set<Product>().AsNoTracking() on l.ElementId equals p.Id
                                where categoryIds.Contains(l.CategoryId) select new { l.CategoryId, Product = p }).ToListAsync(ct).ConfigureAwait(false);
        var productIds = inCategory.Select(x => x.Product.Id).Distinct().ToList();
        var prices = await PricesAsync(productIds, ct).ConfigureAwait(false);
        return [.. menus.Select(m => new SaleMenuDto(m.Id, m.Code, m.Name.In(language), m.StartsAt, m.EndsAt, [.. links.Where(l => l.SaleMenuId == m.Id).Select(l =>
            new MenuCategoryDto(l.ProductCategoryId, categories.GetValueOrDefault(l.ProductCategoryId)?.Name.In(language), [.. inCategory.Where(x => x.CategoryId == l.ProductCategoryId)
                .Select(x => new MenuProductDto(x.Product.Id, x.Product.Code, x.Product.Name.In(language), l.ProductCategoryId, prices.GetValueOrDefault(x.Product.Id)?.Value,
                    prices.GetValueOrDefault(x.Product.Id)?.CurrencyId))]))]))];
    }

    /// <summary>
    /// A product's price: its latest sale price for everyone (no client category or type), in the company's currency at today's rates (the founder,
    /// 2026-11-04); a price whose currency has no rate today is passed over for the one before it.
    /// </summary>
    private async Task<Dictionary<Guid, PosPrice>> PricesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var baseCurrency = await db.Set<Currency>().AsNoTracking().Where(c => c.Active && c.IsBase && c.OwnerId == company).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var rows = (await db.Set<ProductSalePrice>().AsNoTracking().Where(p => p.ProductId != null && productIds.Contains(p.ProductId.Value) && p.PartnerCategoryId == null
                && p.PartnerTypeId == null && p.Value != null).OrderByDescending(p => p.Id).ToListAsync(ct).ConfigureAwait(false)).GroupBy(p => p.ProductId!.Value);
        var prices = new Dictionary<Guid, PosPrice>();
        foreach (var product in rows)
        {
            foreach (var row in product)
            {
                var value = baseCurrency is { } bc ? await Currencies.PriceConversion.ConvertAsync(db, row.Value!.Value, row.CurrencyId, bc, bc, today, ct).ConfigureAwait(false)
                    : row.Value;
                if (value is { } v)
                {
                    prices[product.Key] = new PosPrice(v, baseCurrency ?? row.CurrencyId);
                    break;
                }
            }
        }

        return prices;
    }

    /// <summary>The products the request's company sells that have a barcode, with their prices: the shop reads them all once, so a scan answers at once.</summary>
    public async Task<IReadOnlyList<PosBarcodeDto>> BarcodesAsync(string language, CancellationToken ct)
    {
        var nodes = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
        var values = (await (from v in db.Set<ProductInfo>().AsNoTracking() join t in db.Set<ProductTypeInfo>().AsNoTracking() on v.TypeInfoId equals t.Id
                             where t.Kind == PropertyKind.Text && t.Code == BarcodeCode select new { v.ElementId, v.Value }).ToListAsync(ct).ConfigureAwait(false))
            .Select(v => (v.ElementId, Barcode: (v.Value is JsonObject texts ? texts.Select(t => t.Value).FirstOrDefault() : v.Value)?.ToString().Trim()))
            .Where(v => !string.IsNullOrEmpty(v.Barcode)).ToList();
        var ids = values.Select(v => v.ElementId).Distinct().ToList();
        var products = (await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct).ConfigureAwait(false))
            .Where(p => p.OwnerId is null || nodes.Contains(p.OwnerId.Value)).ToDictionary(p => p.Id);
        var prices = await PricesAsync(products.Keys, ct).ConfigureAwait(false);
        return [.. values.Where(v => products.ContainsKey(v.ElementId)).Select(v =>
        {
            var product = products[v.ElementId];
            var price = prices.GetValueOrDefault(product.Id);
            return new PosBarcodeDto(v.Barcode!, product.Id, product.Code, product.Name.In(language), price?.Value, price?.CurrencyId);
        })];
    }

    /// <summary>The settings that apply at a point of sale (none: the company's default), column by column its own row's, its kind's or the default's.</summary>
    public async Task<PosSettingsDto> SettingsAsync(Guid? pointOfSale, string language, CancellationToken ct)
    {
        // A till works as its shop: its own row first, then the shop's, its kind's and the default's.
        var (shop, chain) = await PosPlaces.ChainAsync(db, await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false), pointOfSale, ct).ConfigureAwait(false);
        Guid? Pick(Func<PointOfSaleSetting, Guid?> column) => chain.Select(column).FirstOrDefault(v => v is not null);
        var (bill, moneyIn, moneyOut, cashDesk, client, method) = (Pick(r => r.BillTypeId), Pick(r => r.MoneyInTypeId), Pick(r => r.MoneyOutTypeId), Pick(r => r.CashDeskId),
            Pick(r => r.WalkInClientId), Pick(r => r.PaymentMethodId));
        var typeIds = new[] { bill, moneyIn, moneyOut, Pick(r => r.OrderTypeId) }.OfType<Guid>().ToList();
        var types = await db.Set<DocumentType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name.In(language), ct).ConfigureAwait(false);
        var desk = cashDesk is { } d ? (await NamesAsync([d], ct).ConfigureAwait(false)).GetValueOrDefault(d) : null;
        var partner = client is { } c ? await db.Set<Partner>().AsNoTracking().Where(p => p.Id == c).Select(p => p.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
        var paymentMethod = method is { } m ? await db.Set<PaymentMethod>().AsNoTracking().Where(p => p.Id == m).Select(p => p.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        var missing = new[] { ("billTypeId", bill), ("moneyInTypeId", moneyIn), ("moneyOutTypeId", moneyOut), ("cashDeskId", cashDesk), ("walkInClientId", client) }
            .Where(x => x.Item2 is null).Select(x => x.Item1).ToList();
        string? Name(Guid? id) => id is { } i ? types.GetValueOrDefault(i) : null;
        var nightProduct = Pick(r => r.ProductId) is { } productId
            ? (await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct).ConfigureAwait(false))?.Name.In(language) : null;
        var shopName = shop is { } at ? (await NamesAsync([at], ct).ConfigureAwait(false)).GetValueOrDefault(at) : null;
        return new PosSettingsDto(shop, bill, Name(bill), moneyIn, Name(moneyIn), moneyOut, Name(moneyOut), cashDesk, desk, client, partner?.In(language), method,
            paymentMethod?.In(language), missing, chain.Select(r => r.Screen).FirstOrDefault(v => v is not null), Pick(r => r.ProductId), chain.Select(r => r.RequiresShift).FirstOrDefault(v => v is not null) ?? true, Pick(r => r.OrderTypeId), Name(Pick(r => r.OrderTypeId)),
            nightProduct, shopName, shop != pointOfSale ? pointOfSale : null);
    }

    /// <summary>
    /// The floor plan of a point of sale (B14): the places below it the request sees, each with its properties and, at the places bills are
    /// written to, the bills of the point of sale's bill type still open there.
    /// </summary>
    public async Task<IReadOnlyList<PosPlaceDto>> PlacesAsync(Guid pointOfSale, string language, CancellationToken ct)
    {
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var centres = (await db.Set<CostCenter>().AsNoTracking().Where(c => c.IdSup != null).Select(c => new { c.Id, c.IdSup, c.Code, c.Name, c.TypeId }).ToListAsync(ct)
            .ConfigureAwait(false)).ToLookup(c => c.IdSup!.Value);
        var places = new List<(Guid Id, Guid Parent, string? Code, TranslatedText Name, Guid TypeId)>();
        for (var level = new List<Guid> { pointOfSale }; level.Count > 0;)
        {
            var below = level.SelectMany(p => centres[p].Select(c => (c.Id, Parent: p, c.Code, c.Name, c.TypeId))).Where(c => places.All(x => x.Id != c.Id)).ToList();
            places.AddRange(below);
            level = [.. below.Select(c => c.Id)];
        }

        places = [.. places.Where(p => nodes.Contains(p.Id))];
        var ids = places.Select(p => p.Id).ToList();
        var properties = await (from v in db.Set<CostCenterInfo>().AsNoTracking() join t in db.Set<CostCenterTypeInfo>().AsNoTracking() on v.TypeInfoId equals t.Id
                                where ids.Contains(v.ElementId) orderby t.SortOrder select new { v.ElementId, t.Id, t.Name, v.Value }).ToListAsync(ct).ConfigureAwait(false);
        var settings = await SettingsAsync(pointOfSale, language, ct).ConfigureAwait(false);
        List<UnpaidBillDto> open = settings.BillTypeId is { } bill ? [.. await UnpaidAsync(bill, null, null, ct).ConfigureAwait(false)] : [];
        if (settings.OrderTypeId is { } orderType)
        {
            open.AddRange(await OpenOrdersAsync(db, await visibility.CompaniesAsync(ct).ConfigureAwait(false), orderType, null, ct).ConfigureAwait(false));
        }

        var writers = open.Where(b => b.CreatedBy != null).Select(b => b.CreatedBy!.Value).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => writers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name.In(language) ?? u.Username, ct)
            .ConfigureAwait(false);
        var parents = places.Select(p => p.Parent).ToHashSet();
        return [.. places.Select(p =>
        {
            var bills = open.Where(b => b.TableId == p.Id).ToList();
            return new PosPlaceDto(p.Id, p.Parent == pointOfSale ? null : p.Parent, p.Code, p.Name.In(language), parents.Contains(p.Id),
                [.. properties.Where(x => x.ElementId == p.Id).Select(x => new PosPropertyDto(x.Id, x.Name.In(language), x.Value))], bills.Count, bills.Sum(b => b.Open),
                [.. bills.Where(b => b.CreatedBy != null).Select(b => b.CreatedBy!.Value).Distinct().Select(u => new PosWaiterDto(u, users.GetValueOrDefault(u)))], p.TypeId);
        })];
    }

    /// <summary>The orders of a type not billed or cancelled yet, of one table or all, in the shape of a bill: what they will come to.</summary>
    internal static async Task<IReadOnlyList<UnpaidBillDto>> OpenOrdersAsync(ErpDbContext db, HashSet<Guid> companies, Guid orderType, Guid? table, CancellationToken ct)
    {
        var billed = db.Set<InterDocumentDocument>().Where(l => l.Active).Join(db.Set<Document>().Where(d => d.Active), l => l.Document2Id, d => d.Id, (l, d) => l.Document1Id);
        var orders = await db.Set<Document>().AsNoTracking().Where(d => d.Active && d.TypeId == orderType && !d.Posted && companies.Contains(d.CompanyId) && (table == null || d.TableId == table) && !billed.Contains(d.Id))
            .OrderBy(d => d.CreatedOn).ToListAsync(ct).ConfigureAwait(false);
        var ids = orders.Select(o => (Guid?)o.Id).ToList();
        var totals = await db.Set<DocumentTotal>().AsNoTracking().Where(t => t.Active && ids.Contains(t.DocumentId)).GroupBy(t => t.DocumentId!.Value)
            .Select(g => new { Id = g.Key, Total = g.Sum(t => t.ValueVatLcu ?? 0) }).ToDictionaryAsync(x => x.Id, x => x.Total, ct).ConfigureAwait(false);
        return [.. orders.Select(o => new UnpaidBillDto(o.Id, o.Code, o.TableId, null, o.ToId, o.ToName, o.CreatedBy, o.CreatedOn, totals.GetValueOrDefault(o.Id), 0, 0, totals.GetValueOrDefault(o.Id)))];
    }

    /// <summary>The posted bills of a type not yet paid in full that the request sees, of one waiter (the user who wrote them) or table, or all.</summary>
    public async Task<IReadOnlyList<UnpaidBillDto>> UnpaidAsync(Guid billTypeId, Guid? user, Guid? table, CancellationToken ct, Guid? pointOfSale = null)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        // A point of sale's bills: their lines leave from it (two points of sale may share a bill type).
        var ofPlace = db.Set<Transaction>().Where(t => t.Active && t.FromId == pointOfSale).Select(t => t.DocumentId);
        var bills = await db.Set<Document>().AsNoTracking().Where(d => d.TypeId == billTypeId && d.Posted && companies.Contains(d.CompanyId)
                && (d.OwnerId == null || nodes.Contains(d.OwnerId.Value)) && (user == null || d.CreatedBy == user) && (table == null || d.TableId == table)
                && (pointOfSale == null || ofPlace.Contains(d.Id)))
            .ToListAsync(ct).ConfigureAwait(false);
        var ids = bills.Select(b => b.Id).ToList();
        var totals = await db.Set<DocumentTotal>().AsNoTracking().Where(t => t.DocumentId != null && ids.Contains(t.DocumentId.Value)).GroupBy(t => t.DocumentId!.Value)
            .Select(g => new { Id = g.Key, Total = g.Sum(t => t.ValueVatLcu ?? 0) }).ToDictionaryAsync(x => x.Id, x => x.Total, ct).ConfigureAwait(false);
        // Paid: the posted documents made from a bill (its payments); reversed: the posted reversals of it.
        var paid = await (from l in db.Set<InterDocumentDocument>().AsNoTracking()
                          join p in db.Set<Document>().AsNoTracking() on l.Document2Id equals p.Id
                          join t in db.Set<DocumentTotal>().AsNoTracking() on p.Id equals t.DocumentId
                          where l.Document1Id != null && ids.Contains(l.Document1Id.Value) && p.Posted && p.ReversedDocumentId == null
                          select new { Bill = l.Document1Id!.Value, Payment = p.Id, Total = t.ValueVatLcu ?? 0 }).ToListAsync(ct).ConfigureAwait(false);
        var shares = await PaidSharesAsync(paid.Select(p => p.Payment).Distinct().ToList(), ct).ConfigureAwait(false);
        var reversed = await (from r in db.Set<Document>().AsNoTracking() join t in db.Set<DocumentTotal>().AsNoTracking() on r.Id equals t.DocumentId
                              where r.ReversedDocumentId != null && ids.Contains(r.ReversedDocumentId.Value) && r.Posted
                              select new { Bill = r.ReversedDocumentId!.Value, Total = t.ValueVatLcu ?? 0 }).ToListAsync(ct).ConfigureAwait(false);
        var tables = await NamesAsync(bills.Where(b => b.TableId != null).Select(b => b.TableId!.Value), ct).ConfigureAwait(false);
        return [.. bills.Select(b =>
        {
            var total = totals.GetValueOrDefault(b.Id);
            var paidAmount = paid.Where(p => p.Bill == b.Id).Sum(p => shares.TryGetValue((p.Payment, b.Id), out var share) ? share : p.Total);
            var reversedAmount = reversed.Where(r => r.Bill == b.Id).Sum(r => r.Total);
            return new UnpaidBillDto(b.Id, b.Code, b.TableId, b.TableId is { } t ? tables.GetValueOrDefault(t) : null, b.ToId, b.ToName, b.CreatedBy, b.CreatedOn, total,
                paidAmount, reversedAmount, total - paidAmount - reversedAmount);
        }).Where(b => b.Open > 0).OrderBy(b => b.CreatedOn)];
    }

    /// <summary>
    /// Pays bills: the given ones, a table's, or every open one of the type; one money document per client into the cash desk (a cash desk line
    /// per bill, its open amount, the bill's code), made from the bills it pays and posted. Returns the payments.
    /// </summary>
    public async Task<IReadOnlyList<DocumentDto>> PayAsync(PayBillsRequest pay, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pay);
        var open = await UnpaidAsync(pay.BillTypeId, null, pay.TableId, ct).ConfigureAwait(false);
        var chosen = pay.All || pay.TableId is not null ? open : [.. open.Where(b => pay.BillIds?.Contains(b.Id) == true)];
        if (pay.BillIds is { Count: > 0 } named && named.FirstOrDefault(id => open.All(b => b.Id != id)) is { } missing && missing != Guid.Empty)
        {
            throw ValidationException.For("billIds", $"Bill {missing} is not an open bill of this type.");
        }

        if (chosen.Count == 0)
        {
            throw ValidationException.For("billIds", "There is no open bill to pay.");
        }

        // The method's money goes where its link at the point of sale says (card payments to the bank, say); else to the settings' cash desk.
        var into = pay.PointOfSaleId is { } place && pay.PaymentMethodId is { } method
            ? await db.Set<InterPaymentMethodCostCenter>().AsNoTracking().Where(l => l.Active && l.CostCenterId == place && l.PaymentMethodId == method && l.CashBankId != null)
                .Select(l => l.CashBankId).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        var cashDesk = await db.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == (into ?? pay.CashDeskId), ct).ConfigureAwait(false)
            ?? throw ValidationException.For("cashDeskId", "There is no such cash desk.");
        var category = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == pay.PaymentTypeId).Select(l => l.CategoryId).FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        // The cash desk line: the payment type's line type whose elements are cost centres of the cash desk's type.
        var lineType = await (from l in db.Set<InterDocumentTypeTransactionType>().AsNoTracking()
                              join c in db.Set<TransactionTypeConf>().AsNoTracking() on l.TransactionTypeId equals c.TransactionTypeId
                              join e in db.Set<Models.Platform.Elements>().AsNoTracking() on c.ElementsId equals e.Id
                              where l.DocumentTypeId == pay.PaymentTypeId && e.TypeId == cashDesk.TypeId && l.TransactionTypeId != null
                              select new { l.TransactionTypeId, Conf = c.Id }).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ValidationException.For("paymentTypeId", $"{ErrorCodes.NotConfiguredError}: the payment type has no line for this cash desk's type.");
        // Money carries no VAT: the 0% group the money document type allows (each company of a tenant has its own).
        var zero = await (from l in db.Set<InterDocumentTypeVatGroup>().AsNoTracking() join v in db.Set<VatGroup>().AsNoTracking() on l.VatGroupId equals v.Id
                          where l.DocumentTypeId == pay.PaymentTypeId && v.Rate == 0 select (Guid?)v.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var date = pay.Date ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var payments = new List<DocumentDto>();
        foreach (var client in chosen.GroupBy(b => (b.ClientId, b.Client)))
        {
            var saved = await documents.SaveAsync(new DocumentDto
            {
                TypeId = pay.PaymentTypeId, CategoryIds = category == Guid.Empty ? [] : [category], DocumentDate = date, PostingDate = date, FromId = client.Key.ClientId,
                FromName = client.Key.Client, ToId = cashDesk.Id, ToName = cashDesk.Name.In(TranslatedText.DefaultLanguage), PaymentMethodId = pay.PaymentMethodId,
                CreatedFromIds = [.. client.Select(b => b.Id)],
                Transactions = [.. client.Select(b => new TransactionDto
                {
                    TypeId = lineType.TransactionTypeId!.Value, TypeConfId = lineType.Conf, ElementId = cashDesk.Id, ElementTypeId = cashDesk.TypeId, ElementName = b.Code, Price = b.Open,
                    Quantity = 1, VatGroupId = zero, ReferenceNumber = b.Code,
                })],
            }, ct).ConfigureAwait(false);
            payments.Add(await documents.PostAsync(saved.Id!.Value, ct).ConfigureAwait(false));
        }

        return payments;
    }

    /// <summary>The payment methods a cost centre offers: only those linked to it, or every method when it has no link.</summary>
    public async Task<PaymentMethodsAtDto> MethodsAtAsync(Guid costCenter, CancellationToken ct)
    {
        var linked = await db.Set<InterPaymentMethodCostCenter>().AsNoTracking().Where(l => l.Active && l.CostCenterId == costCenter && l.PaymentMethodId != null)
            .Select(l => l.PaymentMethodId!.Value).Distinct().ToListAsync(ct).ConfigureAwait(false);
        return new PaymentMethodsAtDto(linked.Count == 0 ? null : linked);
    }

    // How much of a payment went to each bill it pays: its line for that bill (the line's reference is the bill's code).
    private async Task<Dictionary<(Guid Payment, Guid Bill), decimal>> PaidSharesAsync(List<Guid> payments, CancellationToken ct)
    {
        var lines = await (from t in db.Set<Transaction>().AsNoTracking()
                           join l in db.Set<InterDocumentDocument>().AsNoTracking() on t.DocumentId equals l.Document2Id
                           join b in db.Set<Document>().AsNoTracking() on l.Document1Id equals b.Id
                           where t.DocumentId != null && payments.Contains(t.DocumentId.Value) && t.ReferenceNumber != null && t.ReferenceNumber == b.Code
                           select new { Payment = t.DocumentId!.Value, Bill = b.Id, Amount = t.ValueVatLcu ?? 0 }).ToListAsync(ct).ConfigureAwait(false);
        return lines.GroupBy(x => (x.Payment, x.Bill)).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
    }

    private async Task<Dictionary<Guid, string?>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<CostCenter>().AsNoTracking().Where(c => list.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name.In(TranslatedText.DefaultLanguage), ct)
            .ConfigureAwait(false);
    }

    // A menu is open from its start to its end; an end before the start runs past midnight; no times: all day.
    private static bool Open(SaleMenu menu, TimeOnly time)
    {
        var (start, end) = (menu.StartsAt ?? TimeOnly.MinValue, menu.EndsAt ?? TimeOnly.MaxValue);
        return start <= end ? time >= start && time < end : time >= start || time < end;
    }
}

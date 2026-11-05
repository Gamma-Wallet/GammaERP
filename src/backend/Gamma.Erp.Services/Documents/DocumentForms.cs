using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Units;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

/// <summary>A choice of a document's list: a payment method, a unit.</summary>
public sealed record OptionDto(Guid Id, string? Code, string Name);

public sealed record VatGroupOptionDto(Guid Id, string? Code, string Name, decimal Rate);

public sealed record CurrencyOptionDto(Guid Id, string? Code, string Name, bool IsBase);

/// <summary>A side a document or a line picks from: the elements row (a lookup scope, see /lookups), its table and name.</summary>
public sealed record DocumentSideDto(Guid ScopeId, string? Table, string Name);

/// <summary>What a line of a transaction type picks: its elements row, the table, whether it moves stock.</summary>
public sealed record LineScopeDto(Guid ConfId, Guid? ScopeId, string? Table, string Name, int StockDirection);

/// <summary>
/// How a line's fields are filled in for the document type: per field 0 = typed, 1 = chosen from the database,
/// 2 = not needed; null when the type has no fill (every field may be given). With the default quantity and discount.
/// </summary>
public sealed record LineFillDto(int? VatGroup, int? Element, int? Unit, int? Location, int? UnitPrice, int? Discount, int? Quantity, int? Value, int? Vat,
    decimal? DefaultQuantity, decimal? DefaultDiscount);

/// <summary>A kind of line the document type takes: a transaction type with what it picks, how it is filled, and the line's own sides.</summary>
public sealed record LineKindDto(Guid TransactionTypeId, string? Code, string Name, IReadOnlyList<LineScopeDto> Scopes, LineFillDto? Fill, DocumentSideDto? From,
    DocumentSideDto? To);

/// <summary>Everything a document editor needs about one document type, read from its configuration (no rule of its own).</summary>
public sealed record DocumentFormDto(Guid TypeId, string? Code, string Name, bool NeverPosted, bool AllowsPartialReversal, bool IsReversal, bool HoldsOpenItem, bool PaysOpenItems, bool BillsByPlan, DocumentSideDto? From,
    DocumentSideDto? To, IReadOnlyList<OptionDto> PaymentMethods, IReadOnlyList<VatGroupOptionDto> VatGroups, IReadOnlyList<CurrencyOptionDto> Currencies,
    IReadOnlyList<LineKindDto> LineKinds, bool HasReversal = true);

/// <summary>
/// What a line proposes once its product is picked: the product's units (the one asked for, else the first) and a price, which the person may
/// change: the sale price when the partner is on the document's to side (a sale), the last purchase price when it is on the from side (a
/// purchase). Source says which, or is null when nothing was found. ScopeIds are the document type's line scopes (tbl_transaction_type_conf)
/// that hold the product, by their elements row (category, type, the product itself): goods and services go on different lines.
/// </summary>
public sealed record LineProposalDto(IReadOnlyList<OptionDto> Units, Guid? UnitId, decimal? Price, Guid? CurrencyId, Guid? VatGroupId, string? Source,
    IReadOnlyList<Guid> ScopeIds)
{
    /// <summary>Whether the product tracks serial numbers: its line needs one per unit.</summary>
    public bool TracksSerial { get; init; }

    /// <summary>Whether the product tracks lots: its line names one.</summary>
    public bool TracksLot { get; init; }
}

public sealed partial class DocumentService
{
    public const string SalePriceSource = "sale price";
    public const string PurchasePriceSource = "last purchase price";

    /// <summary>The document type's form (B4, C3 phase 4): its sides, payment methods, VAT groups, currencies and kinds of line.</summary>
    public async Task<DocumentFormDto> FormAsync(Guid typeId, string language, CancellationToken ct)
    {
        var allowed = await AllowedTypeAsync(typeId, ct).ConfigureAwait(false);
        var type = await db.Set<DocumentType>().AsNoTracking().FirstAsync(t => t.Id == typeId, ct).ConfigureAwait(false);
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var scopes = await db.Set<Elements>().AsNoTracking().ToDictionaryAsync(e => e.Id, ct).ConfigureAwait(false);
        DocumentSideDto? Side(Guid? scope) => scope is { } s && scopes.TryGetValue(s, out var e) ? new DocumentSideDto(s, e.Table, e.Name?.In(language) ?? e.Table ?? "") : null;

        var from = await db.Set<DocumentTypeConfFrom>().AsNoTracking().Where(c => c.DocumentTypeId == typeId).Select(c => c.ElementsId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var to = await db.Set<DocumentTypeConfTo>().AsNoTracking().Where(c => c.DocumentTypeId == typeId).Select(c => c.ElementsId).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        // Payment methods and VAT groups the type names; none named means every one there is (as saving checks).
        var methodIds = await db.Set<InterPaymentMethodDocumentType>().AsNoTracking().Where(l => l.DocumentTypeId == typeId && l.PaymentMethodId != null)
            .Select(l => l.PaymentMethodId!.Value).ToListAsync(ct).ConfigureAwait(false);
        var methods = await db.Set<PaymentMethod>().AsNoTracking().Where(m => methodIds.Count == 0 || methodIds.Contains(m.Id)).OrderBy(m => m.Code)
            .ToListAsync(ct).ConfigureAwait(false);
        var vatIds = await db.Set<InterDocumentTypeVatGroup>().AsNoTracking().Where(l => l.DocumentTypeId == typeId && l.VatGroupId != null)
            .Select(l => l.VatGroupId!.Value).ToListAsync(ct).ConfigureAwait(false);
        // A type naming none takes the company's own, the highest rate first: every company of a tenant has its own groups.
        var vatOwner = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        var vatGroups = await db.Set<VatGroup>().AsNoTracking().Where(v => vatIds.Count == 0 ? v.OwnerId == vatOwner || v.OwnerId == null : vatIds.Contains(v.Id))
            .OrderBy(v => v.Code).ThenByDescending(v => v.Rate).ToListAsync(ct).ConfigureAwait(false);
        var currencies = await db.Set<Currency>().AsNoTracking().Where(c => c.OwnerId == company || c.OwnerId == null).OrderByDescending(c => c.IsBase).ThenBy(c => c.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        var links = await db.Set<InterDocumentTypeTransactionType>().AsNoTracking().Where(l => l.DocumentTypeId == typeId && l.TransactionTypeId != null)
            .OrderBy(l => l.Id).ToListAsync(ct).ConfigureAwait(false);
        var linkIds = links.Select(l => (Guid?)l.Id).ToList();
        var typeIds = links.Select(l => l.TransactionTypeId!.Value).ToList();
        var transactionTypes = await db.Set<TransactionType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);
        var confs = (await db.Set<TransactionTypeConf>().AsNoTracking().Where(c => c.TransactionTypeId != null && typeIds.Contains(c.TransactionTypeId.Value))
            .OrderBy(c => c.Id).ToListAsync(ct).ConfigureAwait(false)).ToLookup(c => c.TransactionTypeId!.Value);
        var fills = (await db.Set<TransactionFill>().AsNoTracking().Where(f => linkIds.Contains(f.DocumentTypeTransactionTypeId)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(f => f.DocumentTypeTransactionTypeId!.Value).ToDictionary(g => g.Key, g => g.First());
        var lineFrom = (await db.Set<TransactionTypeConfFrom>().AsNoTracking().Where(c => linkIds.Contains(c.DocumentTypeTransactionTypeId)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(c => c.DocumentTypeTransactionTypeId!.Value).ToDictionary(g => g.Key, g => g.First().ElementsId);
        var lineTo = (await db.Set<TransactionTypeConfTo>().AsNoTracking().Where(c => linkIds.Contains(c.DocumentTypeTransactionTypeId)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(c => c.DocumentTypeTransactionTypeId!.Value).ToDictionary(g => g.Key, g => g.First().ElementsId);

        var kinds = links.Where(l => transactionTypes.ContainsKey(l.TransactionTypeId!.Value)).Select(l =>
        {
            var transactionType = transactionTypes[l.TransactionTypeId!.Value];
            var fill = fills.GetValueOrDefault(l.Id);
            return new LineKindDto(transactionType.Id, transactionType.Code, transactionType.Name.In(language) ?? "",
                [.. confs[transactionType.Id].Select(c => new LineScopeDto(c.Id, c.ElementsId, Side(c.ElementsId)?.Table, Side(c.ElementsId)?.Name ?? "", c.StockDirection))],
                fill is null ? null : new LineFillDto(fill.VatGroup, fill.Element, fill.Unit, fill.Location, fill.UnitPriceWithoutVat, fill.Discount, fill.Quantity,
                    fill.PriceWithoutVat, fill.Vat, fill.DefaultQuantity, fill.DefaultDiscount),
                Side(lineFrom.GetValueOrDefault(l.Id)), Side(lineTo.GetValueOrDefault(l.Id)));
        }).ToList();

        return new DocumentFormDto(typeId, allowed.Code, type.Name.In(language) ?? "", type.NeverPosted, type.AllowsPartialReversal, type.IsReversal, type.HoldsOpenItem, type.PaysOpenItems, type.BillsByPlan, Side(from), Side(to),
            [.. methods.Select(m => new OptionDto(m.Id, m.Code, m.Name?.In(language) ?? m.Code ?? ""))],
            [.. vatGroups.Select(v => new VatGroupOptionDto(v.Id, v.Code, v.Name?.In(language) ?? v.Code ?? "", v.Rate ?? 0))],
            [.. currencies.Select(c => new CurrencyOptionDto(c.Id, c.Code, c.Name?.In(language) ?? c.Code ?? "", c.IsBase))],
            kinds, await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Active && t.ReversedDocumentTypeId == typeId, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// The units and the proposed price of a product on a line of the document type (B5, C3 phase 4). Sale prices are matched from the most
    /// particular (the partner's type and category, the unit, the currency) to the general, the latest first; a purchase proposes the
    /// latest price the company paid for the product in that unit.
    /// </summary>
    public async Task<LineProposalDto> LineProposalAsync(Guid typeId, Guid productId, Guid? partnerId, Guid? unitId, Guid? currencyId, string language, CancellationToken ct,
        DateOnly? date = null)
    {
        var proposal = await ProposeAsync(typeId, productId, partnerId, unitId, currencyId, date ?? DateOnly.FromDateTime(DateTime.Today), language, ct).ConfigureAwait(false);
        var product = await (from p in db.Set<Gamma.Erp.Models.Products.Product>().AsNoTracking() join k in db.Set<Gamma.Erp.Models.Products.ProductType>().AsNoTracking() on p.TypeId equals k.Id
                             where p.Id == productId select new { k.TracksSerial, k.TracksLot, Own = p.VatGroupId, OfType = k.VatGroupId }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        // The product's group, else its type's, else what the last purchase carried; the document type may not offer it.
        var vat = product?.Own ?? product?.OfType ?? proposal.VatGroupId;
        var offered = await db.Set<InterDocumentTypeVatGroup>().AsNoTracking().Where(l => l.DocumentTypeId == typeId && l.VatGroupId != null).Select(l => l.VatGroupId!.Value).ToListAsync(ct).ConfigureAwait(false);
        return proposal with { TracksSerial = product?.TracksSerial ?? false, TracksLot = product?.TracksLot ?? false, VatGroupId = vat is { } group && (offered.Count == 0 || offered.Contains(group)) ? group : null };
    }

    private async Task<LineProposalDto> ProposeAsync(Guid typeId, Guid productId, Guid? partnerId, Guid? unitId, Guid? currencyId, DateOnly day, string language, CancellationToken ct)
    {
        await AllowedTypeAsync(typeId, ct).ConfigureAwait(false);
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false);
        // The product's own units, or else its type's, the default first.
        var unitIds = (await new Stock.ProductUnitService(db, request, visibility).OfferedAsync(productId, ct).ConfigureAwait(false)).ToList();

        var units = (await db.Set<Unit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(u => unitIds.IndexOf(u.Id)).Select(u => new OptionDto(u.Id, u.Code, u.Name.In(language) ?? u.Code ?? "")).ToList();
        var unit = unitId is { } asked && units.Any(u => u.Id == asked) ? asked : units.FirstOrDefault()?.Id;
        var specific = unit is null ? null : await db.Set<InterProductSpecificUnit>().AsNoTracking().Where(s => s.ProductId == productId && s.UnitId == unit)
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var scopes = await ScopesHoldingAsync(typeId, productId, ct).ConfigureAwait(false);

        // Which side the partner is on tells a sale from a purchase.
        var partnerTable = db.Model.FindEntityType(typeof(Partner))!.GetTableName();
        var toSide = await (from c in db.Set<DocumentTypeConfTo>().AsNoTracking() join e in db.Set<Elements>().AsNoTracking() on c.ElementsId equals e.Id
                            where c.DocumentTypeId == typeId select e.Table).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var fromSide = await (from c in db.Set<DocumentTypeConfFrom>().AsNoTracking() join e in db.Set<Elements>().AsNoTracking() on c.ElementsId equals e.Id
                              where c.DocumentTypeId == typeId select e.Table).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (toSide == partnerTable)
        {
            var partnerType = partnerId is { } p ? await db.Set<Partner>().AsNoTracking().Where(x => x.Id == p).Select(x => (Guid?)x.TypeId).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
            var partnerCategories = partnerId is { } q ? await db.Set<InterPartnerCategory>().AsNoTracking().Where(l => l.ElementId == q).Select(l => l.CategoryId)
                .ToListAsync(ct).ConfigureAwait(false) : [];
            // The founder (2026-11-04): a price is always its value at the rates of the document's day, never the stored Value LCU (right only on the day it was set).
            var baseCurrency = await db.Set<Currency>().AsNoTracking().Where(c => c.Active && c.IsBase && c.OwnerId == company).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            var target = currencyId ?? baseCurrency;
            async Task<decimal?> AtTheDay(decimal? value, Guid? currency) => value is not { } v ? null
                : baseCurrency is { } bc ? await Currencies.PriceConversion.ConvertAsync(db, v, currency, target, bc, day, ct).ConfigureAwait(false)
                : currency is null || currency == target ? v : null;
            var prices = (await db.Set<ProductSalePrice>().AsNoTracking().Where(s => s.CompanyId == company && s.ProductId == productId
                    && (s.ProductSpecificUnitId == null || s.ProductSpecificUnitId == specific)
                    && (s.PartnerTypeId == null || s.PartnerTypeId == partnerType) && (s.PartnerCategoryId == null || partnerCategories.Contains(s.PartnerCategoryId.Value)))
                .ToListAsync(ct).ConfigureAwait(false))
                .OrderByDescending(s => s.PartnerTypeId is not null).ThenByDescending(s => s.PartnerCategoryId is not null).ThenByDescending(s => s.ProductSpecificUnitId is not null)
                .ThenByDescending(s => (s.CurrencyId ?? baseCurrency) == target).ThenByDescending(s => s.Id).ToList();
            decimal? price = null;
            foreach (var row in prices)
            {
                // A row whose currency has no rate on the day is passed over: the next that fits is taken.
                if (await AtTheDay(row.Value, row.CurrencyId).ConfigureAwait(false) is { } worked)
                {
                    price = worked;
                    break;
                }
            }
            // With no price of the product's own, its type's price for the unit (Sale prices by partner), the closest match to the partner first.
            // Only rows this company's configuration reads: its own, or the group's above it.
            var configuration = await visibility.ConfigurationNodesAsync(ct).ConfigureAwait(false);
            var typePrices = price is not null || unit is null ? [] : (await (from s in db.Set<ProductSalePriceType>().AsNoTracking()
                    join u in db.Set<InterProductTypeUnit>().AsNoTracking() on s.ProductTypeUnitId equals (Guid?)u.Id
                    join product in db.Set<Product>().AsNoTracking() on u.ProductTypeId equals (Guid?)product.TypeId
                    where product.Id == productId && u.UnitId == unit && s.Active && u.Active && (s.OwnerId == null || configuration.Contains(s.OwnerId.Value))
                        && (s.PartnerTypeId == null || s.PartnerTypeId == partnerType) && (s.PartnerCategoryId == null || partnerCategories.Contains(s.PartnerCategoryId.Value))
                    select s).ToListAsync(ct).ConfigureAwait(false))
                .OrderByDescending(s => s.PartnerTypeId is not null).ThenByDescending(s => s.PartnerCategoryId is not null).ThenByDescending(s => (s.CurrencyId ?? baseCurrency) == target)
                .ThenByDescending(s => s.Id).ToList();
            foreach (var row in typePrices)
            {
                if (price is null && await AtTheDay(row.Value, row.CurrencyId).ConfigureAwait(false) is { } worked)
                {
                    price = worked;
                }
            }
            // A fixed asset sells at what is left of its value; the user may change the price.
            if (company is { } seller && await new Depreciation.AssetSale(db).SellsAssetsAsync(typeId, ct).ConfigureAwait(false)
                && await new Depreciation.AssetSale(db).RemainingValueAsync(seller, productId, unit, ct).ConfigureAwait(false) is { } left)
            {
                return new LineProposalDto(units, unit, left, null, null, SalePriceSource, scopes);
            }

            return new LineProposalDto(units, unit, price, price is null ? null : target, null, price is null ? null : SalePriceSource, scopes);
        }

        if (fromSide == partnerTable && specific is not null)
        {
            var paid = await db.Set<ProductPurchasePrice>().AsNoTracking().Where(s => s.CompanyId == company && s.ProductId == productId && s.ProductSpecificUnitId == specific
                    && (currencyId == null || s.CurrencyId == currencyId))
                .OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            // The purchase price row keeps the line's value: the unit price is the purchase line's own.
            var price = paid?.DocumentId is { } bill ? await db.Set<Transaction>().AsNoTracking()
                .Where(t => t.DocumentId == bill && t.ElementId == productId && t.UnitId == unit).Select(t => t.Price).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
            return new LineProposalDto(units, unit, price, paid?.CurrencyId, paid?.VatGroupId, price is null ? null : PurchasePriceSource, scopes);
        }

        return new LineProposalDto(units, unit, null, null, null, null, scopes);
    }

    /// <summary>
    /// The document type's line scopes on products that hold the product: their elements row's category is one the product is in or under (or
    /// none), its type the product's (or none), its record the product (or none).
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ScopesHoldingAsync(Guid typeId, Guid productId, CancellationToken ct)
    {
        var productTable = db.Model.FindEntityType(typeof(Product))!.GetTableName();
        var scopes = await (from l in db.Set<InterDocumentTypeTransactionType>().AsNoTracking()
                            join c in db.Set<TransactionTypeConf>().AsNoTracking() on l.TransactionTypeId equals c.TransactionTypeId
                            join e in db.Set<Elements>().AsNoTracking() on c.ElementsId equals e.Id
                            where l.DocumentTypeId == typeId && e.Table == productTable
                            select new { c.Id, e.CategoryId, e.TypeId, e.RecordId }).ToListAsync(ct).ConfigureAwait(false);
        var product = await db.Set<Product>().AsNoTracking().Where(p => p.Id == productId).Select(p => new { p.TypeId }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (product is null)
        {
            return [];
        }

        // The product's categories and every one above them.
        var parents = await db.Set<ProductCategory>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.IdSup, ct).ConfigureAwait(false);
        var categories = new HashSet<Guid>();
        foreach (var own in await db.Set<InterProductCategory>().AsNoTracking().Where(l => l.ElementId == productId).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false))
        {
            for (Guid? at = own; at is { } c && categories.Add(c);)
            {
                at = parents.GetValueOrDefault(c);
            }
        }

        return [.. scopes.Where(s => (s.CategoryId is null || categories.Contains(s.CategoryId.Value)) && (s.TypeId is null || s.TypeId == product.TypeId)
            && (s.RecordId is null || s.RecordId == productId)).Select(s => s.Id).Distinct()];
    }
}

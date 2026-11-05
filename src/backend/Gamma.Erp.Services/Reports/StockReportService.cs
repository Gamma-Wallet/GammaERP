using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Reports;

/// <summary>A product at a place: how much there is now and its value, what is left of the place's cost layers (first in, first out).</summary>
public sealed record StockRowDto(Guid CostCenterId, string? CostCenter, Guid ProductId, string? Code, string? Product, Guid? UnitId, string? Unit, decimal Quantity,
    decimal UnitCost, decimal Value, string Holder = StockReportService.PlaceHolder);

/// <summary>One movement of a product: the document, from where to where, how much.</summary>
public sealed record ItemMovementDto(DateOnly? PostingDate, Guid DocumentId, string? Document, string? DocumentType, string? From, string? To, decimal Quantity,
    string? Unit, decimal Value);

/// <summary>A line of the sales or purchase report; a reversal counts negative.</summary>
public sealed record DocumentLineRowDto(DateOnly? PostingDate, Guid DocumentId, string? Document, string? Partner, string? CostCenter, string? Item, decimal Quantity,
    string? Unit, decimal Value, decimal Vat, decimal Total, Guid? PartnerId = null, Guid? CostCenterId = null, string? From = null, string? To = null, decimal Cost = 0, Guid? UserId = null, string? User = null, Guid? ItemId = null);

public sealed record LineGroupDto(string? Name, decimal Quantity, decimal Value, decimal Vat, decimal Total, Guid? Id = null);

public sealed record DocumentLinesDto(IReadOnlyList<DocumentLineRowDto> Lines, IReadOnlyList<LineGroupDto> ByCostCenter, IReadOnlyList<LineGroupDto> ByPartner,
    decimal Value, decimal Vat, decimal Total, IReadOnlyList<LineGroupDto>? ByUser = null);

/// <summary>A purchase of a product: the line's quantity and value in the company's currency, its unit price, and the reversal if it was reversed.</summary>
public sealed record PurchasePriceRowDto(DateOnly? PostingDate, Guid DocumentId, string? Document, string? Supplier, decimal Quantity, string? Unit, decimal UnitPrice, decimal Value,
    string? ReversedBy);

/// <summary>
/// The stock and sales reports (Inventory / asset and goods balance per cost centre, item tracking, sales and purchases
/// per cost centre or partner), from the stock rows, the posted documents and their lines of the companies the request sees.
/// </summary>
public sealed class StockReportService(ErpDbContext db, Visibility visibility)
{
    /// <summary>Who a stock row is at: a cost centre, a client or supplier, a member of staff, or all three.</summary>
    public const string PlaceHolder = "place";

    public const string PartnerHolder = "partner";

    public const string StaffHolder = "staff";

    public const string AllHolders = "all";

    private sealed record Held(Guid HolderId, string Holder, Guid ProductId, Guid? UnitId, Guid ProductSpecificUnitId, decimal Quantity);

    /// <summary>What each place holds now.</summary>
    public Task<IReadOnlyList<StockRowDto>> StockAsync(Guid? costCenterId, Guid? productCategoryId, string language, CancellationToken ct) =>
        StockAsync(costCenterId, productCategoryId, PlaceHolder, language, ct);

    /// <summary>
    /// What each holder has now (one place, or all; one product category, or all), valued by what is left of its cost layers. Clients, suppliers and
    /// staff show their running position: what they received is positive, what they delivered negative.
    /// </summary>
    public async Task<IReadOnlyList<StockRowDto>> StockAsync(Guid? costCenterId, Guid? productCategoryId, string holder, string language, CancellationToken ct)
    {
        if (holder is not (PlaceHolder or PartnerHolder or StaffHolder or AllHolders))
        {
            throw ValidationException.For("holder", "Name the holder: place, partner, staff or all.");
        }

        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var stock = new List<Held>();
        if (holder is PlaceHolder or AllHolders)
        {
            stock.AddRange(await (from s in db.Set<InterCostCenterProductStock>().AsNoTracking()
                                  join u in db.Set<InterProductSpecificUnit>().AsNoTracking() on s.ProductSpecificUnitId equals u.Id
                                  where companies.Contains(s.CompanyId) && s.Active && s.CostCenterId != null && s.Quantity != 0 && (costCenterId == null || s.CostCenterId == costCenterId) && u.ProductId != null
                                  select new Held(s.CostCenterId!.Value, PlaceHolder, u.ProductId!.Value, u.UnitId, s.ProductSpecificUnitId!.Value, s.Quantity ?? 0)).ToListAsync(ct).ConfigureAwait(false));
        }

        if (costCenterId is null && holder is PartnerHolder or AllHolders)
        {
            stock.AddRange(await (from s in db.Set<InterPartnerProductStock>().AsNoTracking()
                                  join u in db.Set<InterProductSpecificUnit>().AsNoTracking() on s.ProductSpecificUnitId equals u.Id
                                  where companies.Contains(s.CompanyId) && s.Active && s.PartnerId != null && s.Quantity != 0 && u.ProductId != null
                                  select new Held(s.PartnerId!.Value, PartnerHolder, u.ProductId!.Value, u.UnitId, s.ProductSpecificUnitId!.Value, s.Quantity ?? 0)).ToListAsync(ct).ConfigureAwait(false));
        }

        if (costCenterId is null && holder is StaffHolder or AllHolders)
        {
            stock.AddRange(await (from s in db.Set<InterEmployeeProductStock>().AsNoTracking()
                                  join u in db.Set<InterProductSpecificUnit>().AsNoTracking() on s.ProductSpecificUnitId equals u.Id
                                  where companies.Contains(s.CompanyId) && s.Active && s.EmployeeId != null && s.Quantity != 0 && u.ProductId != null
                                  select new Held(s.EmployeeId!.Value, StaffHolder, u.ProductId!.Value, u.UnitId, s.ProductSpecificUnitId!.Value, s.Quantity ?? 0)).ToListAsync(ct).ConfigureAwait(false));
        }

        if (productCategoryId is { } category)
        {
            var inCategory = (await db.Set<InterProductCategory>().AsNoTracking().Where(l => l.CategoryId == category).Select(l => l.ElementId)
                .ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
            stock = [.. stock.Where(s => inCategory.Contains(s.ProductId))];
        }

        // A place's value of a product-unit: what is left of its cost layers.
        var specifics = stock.Select(s => s.ProductSpecificUnitId).Distinct().ToList();
        var values = (await db.Set<ProductCostLayer>().AsNoTracking().Where(l => l.Active && specifics.Contains(l.ProductSpecificUnitId) && companies.Contains(l.CompanyId))
                .GroupBy(l => new { l.HolderId, l.ProductSpecificUnitId }).Select(g => new { g.Key.HolderId, g.Key.ProductSpecificUnitId, Value = g.Sum(l => l.ValueLeftLcu) })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(x => (x.HolderId, x.ProductSpecificUnitId), x => x.Value);

        var products = await Names<Product>(stock.Select(s => s.ProductId), ct).ConfigureAwait(false);
        var holders = await Names<CostCenter>(stock.Where(s => s.Holder == PlaceHolder).Select(s => s.HolderId), ct).ConfigureAwait(false);
        foreach (var (id, name) in await Names<Models.Partners.Partner>(stock.Where(s => s.Holder == PartnerHolder).Select(s => s.HolderId), ct).ConfigureAwait(false))
        {
            holders[id] = name;
        }

        foreach (var (id, name) in await Names<Models.Employees.Employee>(stock.Where(s => s.Holder == StaffHolder).Select(s => s.HolderId), ct).ConfigureAwait(false))
        {
            holders[id] = name;
        }

        var units = await Names<Models.Units.Unit>(stock.Where(s => s.UnitId != null).Select(s => s.UnitId!.Value), ct).ConfigureAwait(false);
        return [.. stock.Select(s =>
        {
            var value = values.GetValueOrDefault((s.HolderId, s.ProductSpecificUnitId));
            var product = products.GetValueOrDefault(s.ProductId);
            return new StockRowDto(s.HolderId, holders.GetValueOrDefault(s.HolderId).Name?.In(language), s.ProductId, product.Code,
                product.Name?.In(language), s.UnitId, s.UnitId is { } u ? units.GetValueOrDefault(u).Name?.In(language) : null, s.Quantity,
                s.Quantity == 0 ? 0 : Math.Round(value / s.Quantity, 4), value, s.Holder);
        }).OrderBy(r => r.Holder).ThenBy(r => r.CostCenter).ThenBy(r => r.Product)];
    }

    /// <summary>A product's movements in a period, in posting order.</summary>
    public async Task<IReadOnlyList<ItemMovementDto>> ItemTrackingAsync(Guid productId, DateOnly since, DateOnly until, string language, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var rows = await (from t in db.Set<Transaction>().AsNoTracking()
                          join d in db.Set<Document>().AsNoTracking() on t.DocumentId equals d.Id
                          where t.ElementId == productId && d.Posted && !db.Set<DocumentType>().Where(k => k.NeverPosted).Select(k => k.Id).Contains(d.TypeId) && companies.Contains(d.CompanyId) && d.PostingDate >= since && d.PostingDate <= until
                          orderby d.PostingDate, d.Id
                          select new { d.PostingDate, d.Id, d.Code, d.TypeId, From = t.FromName ?? d.FromName, To = t.ToName ?? d.ToName, t.Quantity, t.UnitName, t.ValueLcu })
            .ToListAsync(ct).ConfigureAwait(false);
        var typeIds = rows.Select(r => r.TypeId).Distinct().ToList();
        var types = await db.Set<DocumentType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct).ConfigureAwait(false);
        return [.. rows.Select(r => new ItemMovementDto(r.PostingDate, r.Id, r.Code, types.GetValueOrDefault(r.TypeId)?.In(language), r.From, r.To, r.Quantity ?? 0,
            r.UnitName, r.ValueLcu ?? 0))];
    }

    /// <summary>
    /// The lines of the posted documents of the given categories in a period (the sales or the purchases), of one cost centre or partner or all,
    /// with their totals per cost centre, per partner and per user who wrote them (a waiter's sales); a document of a reversal type counts negative.
    /// </summary>
    public async Task<DocumentLinesDto> LinesAsync(IReadOnlyList<Guid> categoryIds, DateOnly since, DateOnly until, Guid? costCenterId, Guid? partnerId, string language,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(categoryIds);
        if (categoryIds.Count == 0)
        {
            throw ValidationException.For("category", "Name at least one document category.");
        }

        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var types = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => categoryIds.Contains(l.CategoryId)).Select(l => l.TypeId).ToListAsync(ct).ConfigureAwait(false);
        var reversals = await db.Set<DocumentType>().AsNoTracking().Where(t => t.IsReversal && t.ReversedDocumentTypeId != null && types.Contains(t.ReversedDocumentTypeId.Value))
            .Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false);
        var included = types.Concat(reversals).ToHashSet();
        var rows = await (from t in db.Set<Transaction>().AsNoTracking()
                          join d in db.Set<Document>().AsNoTracking() on t.DocumentId equals d.Id
                          where d.Posted && included.Contains(d.TypeId) && companies.Contains(d.CompanyId) && d.PostingDate >= since && d.PostingDate <= until
                              && (costCenterId == null || d.FromId == costCenterId || d.ToId == costCenterId || t.FromId == costCenterId || t.ToId == costCenterId)
                              && (partnerId == null || d.FromId == partnerId || d.ToId == partnerId)
                          orderby d.PostingDate, d.Id
                          select new { d.PostingDate, d.Id, d.Code, d.TypeId, d.FromId, d.FromName, d.ToId, d.ToName, d.CreatedBy, LineFrom = t.FromId, t.ElementId, t.ElementName, t.Quantity, t.UnitName,
                              t.ValueLcu, t.VatLcu, t.ValueVatLcu, t.CostLcu }).ToListAsync(ct).ConfigureAwait(false);
        var sides = rows.SelectMany(r => new[] { r.FromId, r.ToId, r.LineFrom }).OfType<Guid>().Distinct().ToList();
        var partners = (await db.Set<Models.Partners.Partner>().AsNoTracking().Where(p => sides.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var centres = await Names<CostCenter>(sides, ct).ConfigureAwait(false);
        var writers = rows.Select(r => r.CreatedBy).OfType<Guid>().Distinct().ToList();
        var users = await db.Set<Models.Users.User>().AsNoTracking().Where(u => writers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name.In(language) ?? u.Username, ct).ConfigureAwait(false);
        var negative = reversals.ToHashSet();
        var lines = rows.Select(r =>
        {
            var sign = negative.Contains(r.TypeId) ? -1 : 1;
            var partner = r.ToId is { } t && partners.Contains(t) ? r.ToName : r.FromId is { } f && partners.Contains(f) ? r.FromName : null;
            var partnerId = r.ToId is { } toPartner && partners.Contains(toPartner) ? toPartner : r.FromId is { } fromPartner && partners.Contains(fromPartner) ? fromPartner : (Guid?)null;
            var centreId = new[] { r.LineFrom, r.FromId, r.ToId }.OfType<Guid>().Where(centres.ContainsKey).Cast<Guid?>().FirstOrDefault();
            var centre = centreId is { } id ? centres[id].Name?.In(language) : null;
            return new DocumentLineRowDto(r.PostingDate, r.Id, r.Code, partner, centre, r.ElementName, sign * (r.Quantity ?? 0), r.UnitName, sign * (r.ValueLcu ?? 0),
                sign * (r.VatLcu ?? 0), sign * (r.ValueVatLcu ?? 0), partnerId, centreId, r.FromName, r.ToName, sign * (r.CostLcu ?? 0), r.CreatedBy, r.CreatedBy is { } u ? users.GetValueOrDefault(u) : null, r.ElementId);
        }).ToList();
        static List<LineGroupDto> By(IEnumerable<DocumentLineRowDto> lines, Func<DocumentLineRowDto, (Guid? Id, string? Name)> key) =>
            [.. lines.GroupBy(key).Select(g => new LineGroupDto(g.Key.Name, g.Sum(l => l.Quantity), g.Sum(l => l.Value), g.Sum(l => l.Vat), g.Sum(l => l.Total), g.Key.Id)).OrderBy(g => g.Name)];
        return new DocumentLinesDto(lines, By(lines, l => (l.CostCenterId, l.CostCenter)), By(lines, l => (l.PartnerId, l.Partner)), lines.Sum(l => l.Value), lines.Sum(l => l.Vat), lines.Sum(l => l.Total),
            By(lines, l => (l.UserId, l.User)));
    }

    /// <summary>What a product was bought at in a period, oldest first: the lines of the documents that recorded a purchase price. A reversed purchase stays, with its reversal.</summary>
    public async Task<IReadOnlyList<PurchasePriceRowDto>> PurchasePricesAsync(Guid productId, DateOnly since, DateOnly until, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var bought = await db.Set<ProductPurchasePrice>().AsNoTracking().Where(p => p.Active && p.ProductId == productId && p.DocumentId != null && companies.Contains(p.CompanyId))
            .Select(p => p.DocumentId!.Value).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var lines = await (from t in db.Set<Transaction>().AsNoTracking()
                           join d in db.Set<Document>().AsNoTracking() on t.DocumentId equals d.Id
                           where t.Active && d.Active && d.Posted && t.ElementId == productId && bought.Contains(d.Id) && d.PostingDate >= since && d.PostingDate <= until
                           orderby d.PostingDate, d.Id
                           select new { d.PostingDate, d.Id, d.Code, d.FromName, t.Quantity, t.UnitName, Value = t.ValueLcu ?? t.Value ?? 0 }).ToListAsync(ct).ConfigureAwait(false);
        var ids = lines.Select(l => l.Id).Distinct().ToList();
        var reversals = (await db.Set<Document>().AsNoTracking().Where(d => d.Active && d.Posted && d.ReversedDocumentId != null && ids.Contains(d.ReversedDocumentId.Value))
                .Select(d => new { Reversed = d.ReversedDocumentId!.Value, d.Code }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(r => r.Reversed).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => r.Code)));
        return [.. lines.Select(l => new PurchasePriceRowDto(l.PostingDate, l.Id, l.Code, l.FromName, l.Quantity ?? 0, l.UnitName, l.Quantity is > 0 ? Math.Round(l.Value / l.Quantity.Value, 4) : 0, l.Value,
            reversals.GetValueOrDefault(l.Id)))];
    }

    private async Task<Dictionary<Guid, (string? Code, Models.Base.TranslatedText? Name)>> Names<T>(IEnumerable<Guid> ids, CancellationToken ct)
        where T : Models.Base.Element
    {
        var list = ids.Distinct().ToList();
        return await db.Set<T>().AsNoTracking().Where(e => list.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => ((string?)e.Code, (Models.Base.TranslatedText?)e.Name), ct)
            .ConfigureAwait(false);
    }
}

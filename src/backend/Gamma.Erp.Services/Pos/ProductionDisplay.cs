using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Stock;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Pos;

public sealed record ProductionPointDto(Guid Id, string? Name);

public sealed record ProductionItemDto(Guid LineId, string? Name, decimal Quantity, string? Unit);

/// <summary>One order's lines waiting at a production point.</summary>
public sealed record ProductionCardDto(Guid OrderId, string? Code, string? Table, string? Waiter, DateTimeOffset SentOn, IReadOnlyList<ProductionItemDto> Items);

/// <summary>A line produced lately; one with a production document can't be taken back.</summary>
public sealed record ProducedItemDto(Guid LineId, string? Name, decimal Quantity, string? Code, string? Table, DateTimeOffset ProducedOn, string? ProductionDocument, bool CanUndo);

public sealed record ProductionBoardDto(int WarningMinutes, IReadOnlyList<ProductionCardDto> Cards, IReadOnlyList<ProducedItemDto> Produced);

public sealed record ProductionLinesRequest(IReadOnlyList<Guid> LineIds);

/// <summary>
/// The production point display (the founder, 2026-11-02). It reads the lines of the orders whose document type sends them to production, routes each to a
/// production point by configuration, and a person says each was produced. For a product with a recipe that makes the production document the order's type names: the recipe's quantities
/// leave the production point, the product comes into the point of sale, and the bill later takes it. That can't be undone: a cancelled order leaves the product in stock. An item with no
/// recipe is only marked.
/// </summary>
public sealed class ProductionDisplay(ErpDbContext db, RequestContext request, Visibility visibility, TimeProvider clock, ProductionService production)
{
    private const int DefaultWarningMinutes = 15, ShownProduced = 20, NotRoutedDays = 7;

    private sealed record OpenLine(Transaction Line, Document Order, Guid? Point);

    /// <summary>The production points of the company.</summary>
    public async Task<IReadOnlyList<ProductionPointDto>> PointsAsync(string language, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var types = db.Set<CostCenterType>().Where(t => t.IsProductionPoint).Select(t => t.Id);
        var points = await db.Set<CostCenter>().AsNoTracking().Where(c => c.Active && types.Contains(c.TypeId) && c.OwnerId != null && companies.Contains(c.OwnerId.Value)).ToListAsync(ct).ConfigureAwait(false);
        return [.. points.Select(p => new ProductionPointDto(p.Id, p.Name.In(language) ?? p.Code)).OrderBy(p => p.Name)];
    }

    /// <summary>What a production point has to make, oldest first, and what it produced last.</summary>
    public async Task<ProductionBoardDto> BoardAsync(Guid point, string language, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var here = (await OpenLinesAsync(company, DateTimeOffset.MinValue, null, ct).ConfigureAwait(false)).Where(l => l.Point == point).ToList();
        var ids = here.Select(l => l.Line.Id).ToList();
        var produced = await db.Set<ProducedLine>().AsNoTracking().Where(d => d.Active && d.CompanyId == company && d.ProductionPointId == point && ids.Contains(d.BillLineId)).ToListAsync(ct).ConfigureAwait(false);
        var producedIds = produced.Select(d => d.BillLineId).ToHashSet();
        var orders = here.Select(l => l.Order).ToList();
        var tableIds = orders.Select(b => b.TableId).OfType<Guid>().Distinct().ToList();
        var tables = await db.Set<CostCenter>().AsNoTracking().Where(c => tableIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name.In(language) ?? c.Code, ct).ConfigureAwait(false);
        var writers = orders.Select(b => b.CreatedBy).OfType<Guid>().Distinct().ToList();
        var waiters = await db.Set<User>().AsNoTracking().Where(u => writers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name.In(language) ?? u.Username, ct).ConfigureAwait(false);
        var madeIds = produced.Select(d => d.ProductionDocumentId).OfType<Guid>().ToList();
        var made = await db.Set<Document>().AsNoTracking().Where(d => madeIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Code, ct).ConfigureAwait(false);
        string? Table(Document order) => order.TableId is { } t ? tables.GetValueOrDefault(t) : null;

        var cards = here.Where(l => !producedIds.Contains(l.Line.Id)).GroupBy(l => l.Order.Id).Select(g =>
        {
            var order = g.First().Order;
            return new ProductionCardDto(order.Id, order.Code, Table(order), order.CreatedBy is { } w ? waiters.GetValueOrDefault(w) : null, order.CreatedOn,
                [.. g.OrderBy(x => x.Line.Code).Select(x => new ProductionItemDto(x.Line.Id, x.Line.ElementName, x.Line.Quantity ?? 0, x.Line.UnitName))]);
        }).OrderBy(c => c.SentOn).ToList();
        var byLine = here.ToDictionary(l => l.Line.Id);
        var recent = produced.Where(d => byLine.ContainsKey(d.BillLineId)).OrderByDescending(d => d.ProducedOn).Take(ShownProduced).Select(d =>
        {
            var line = byLine[d.BillLineId];
            return new ProducedItemDto(d.BillLineId, line.Line.ElementName, line.Line.Quantity ?? 0, line.Order.Code, Table(line.Order), d.ProducedOn,
                d.ProductionDocumentId is { } m ? made.GetValueOrDefault(m) : null, d.ProductionDocumentId is null);
        }).ToList();
        var warning = await db.Set<ProductionRoute>().AsNoTracking().Where(r => r.Active && r.OwnerId == company && r.ProductionPointId == point && r.WarningMinutes != null)
            .MinAsync(r => r.WarningMinutes, ct).ConfigureAwait(false);
        return new ProductionBoardDto(warning ?? DefaultWarningMinutes, cards, recent);
    }

    /// <summary>A person says lines were produced, all or none: each with a recipe makes its production document.</summary>
    public async Task ProducedAsync(Guid point, IReadOnlyList<Guid> lineIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lineIds);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var open = (await OpenLinesAsync(company, DateTimeOffset.MinValue, null, ct).ConfigureAwait(false)).Where(l => l.Point == point).ToDictionary(l => l.Line.Id);
        if (lineIds.Any(l => !open.ContainsKey(l)))
        {
            throw ValidationException.For("lineIds", "This line is not open at this production point.");
        }

        var done = (await db.Set<ProducedLine>().AsNoTracking().Where(d => d.Active && lineIds.Contains(d.BillLineId)).Select(d => d.BillLineId).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var lines = lineIds.Distinct().Where(l => !done.Contains(l)).Select(l => open[l]).ToList();
        var products = lines.Select(l => l.Line.ElementId!.Value).Distinct().ToList();
        var withRecipe = (await db.Set<ProductRecipe>().AsNoTracking().Where(r => r.Active && products.Contains(r.ProductId) && r.Quantity > 0).Select(r => r.ProductId).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var typeIds = lines.Select(l => l.Order.TypeId).Distinct().ToList();
        var types = await db.Set<DocumentType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.ProductionDocumentTypeId, ct).ConfigureAwait(false);
        var day = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var line in lines)
        {
            Guid? made = null;
            if (withRecipe.Contains(line.Line.ElementId!.Value))
            {
                var type = types.GetValueOrDefault(line.Order.TypeId) ?? throw ValidationException.For("typeId", $"{ErrorCodes.NotConfiguredError}: this order's document type names no production document.");
                var place = line.Line.FromId ?? line.Order.FromId ?? throw ValidationException.For("lineIds", "This line has no point of sale.");
                made = (await production.ProduceAsync(new ProduceRequest(point, line.Line.ElementId!.Value, line.Line.UnitId!.Value, line.Line.Quantity ?? 0, day, ToCostCenterId: place, TypeId: type, DeliverAnywhereInCompany: true), ct).ConfigureAwait(false)).Id;
            }

            db.Add(new ProducedLine
            {
                CompanyId = company, BillLineId = line.Line.Id, ProductionPointId = point, ProductionDocumentId = made, ProducedBy = request.UserId, ProducedOn = clock.GetUtcNow(),
                TenantId = request.TenantId, OwnerId = company,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Takes lines back to do; refused once a production document was made, since what was made can't be undone.</summary>
    public async Task UndoAsync(Guid point, IReadOnlyList<Guid> lineIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lineIds);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var rows = await db.Set<ProducedLine>().Where(d => d.Active && d.CompanyId == company && d.ProductionPointId == point && lineIds.Contains(d.BillLineId)).ToListAsync(ct).ConfigureAwait(false);
        if (rows.Any(r => r.ProductionDocumentId is not null))
        {
            throw ValidationException.For("lineIds", "This product was produced and can't be undone.");
        }

        rows.ForEach(r => r.Active = false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>What was ordered lately with no production point, so nothing is lost.</summary>
    public async Task<IReadOnlyList<string>> NotRoutedAsync(CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var lines = await OpenLinesAsync(company, clock.GetUtcNow().AddDays(-NotRoutedDays), null, ct).ConfigureAwait(false);
        return [.. lines.Where(l => l.Point is null).Select(l => l.Line.ElementName ?? string.Empty).Distinct().Order()];
    }

    /// <summary>The names on these orders that a production point still has to produce: a bill waits for them.</summary>
    public async Task<IReadOnlyList<string>> NotProducedAsync(Guid company, IReadOnlyCollection<Guid> orderIds, CancellationToken ct)
    {
        var lines = (await OpenLinesAsync(company, DateTimeOffset.MinValue, orderIds, ct).ConfigureAwait(false)).Where(l => l.Point is not null).ToList();
        var ids = lines.Select(l => l.Line.Id).ToList();
        var done = (await db.Set<ProducedLine>().AsNoTracking().Where(d => d.Active && ids.Contains(d.BillLineId)).Select(d => d.BillLineId).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        return [.. lines.Where(l => !done.Contains(l.Line.Id)).Select(l => l.Line.ElementName ?? string.Empty).Distinct()];
    }

    // The product lines of the open orders whose type sends them to production, however old (an order still open must be producible, or its bill waits for ever), each with the production point its route names. An order leaves once it is billed or cancelled.
    private async Task<List<OpenLine>> OpenLinesAsync(Guid company, DateTimeOffset since, IReadOnlyCollection<Guid>? onlyOrders, CancellationToken ct)
    {
        var types = db.Set<DocumentType>().Where(t => t.SendsToProduction && t.OwnerId == company).Select(t => t.Id);
        var billed = db.Set<InterDocumentDocument>().Where(l => l.Active).Join(db.Set<Document>().Where(d => d.Active), l => l.Document2Id, d => d.Id, (l, d) => l.Document1Id);
        var orders = await db.Set<Document>().AsNoTracking().Where(d => d.Active && d.CompanyId == company && types.Contains(d.TypeId) && d.CreatedOn >= since && !billed.Contains(d.Id)
            && (onlyOrders == null || onlyOrders.Contains(d.Id))).ToDictionaryAsync(d => d.Id, ct).ConfigureAwait(false);
        var orderIds = orders.Keys.Select(b => (Guid?)b).ToList();
        var lines = await db.Set<Transaction>().AsNoTracking().Where(t => t.Active && orderIds.Contains(t.DocumentId) && t.ElementId != null).ToListAsync(ct).ConfigureAwait(false);
        var elements = lines.Select(l => l.ElementId!.Value).Distinct().ToList();
        var products = (await db.Set<Product>().AsNoTracking().Where(p => elements.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var categories = (await db.Set<InterProductCategory>().AsNoTracking().Where(l => l.Active && elements.Contains(l.ElementId)).ToListAsync(ct).ConfigureAwait(false)).ToLookup(l => l.ElementId, l => l.CategoryId);
        var routes = await db.Set<ProductionRoute>().AsNoTracking().Where(r => r.Active && r.OwnerId == company).ToListAsync(ct).ConfigureAwait(false);

        Guid? Route(Guid product, Guid? place) => routes.Where(r => (r.PointOfSaleId == null || r.PointOfSaleId == place) && (r.ProductId == product || (r.ProductId == null && r.ProductCategoryId is { } c && categories[product].Contains(c))))
            .OrderByDescending(r => r.ProductId != null).ThenByDescending(r => r.PointOfSaleId != null).Select(r => (Guid?)r.ProductionPointId).FirstOrDefault();
        return [.. lines.Where(l => products.Contains(l.ElementId!.Value)).Select(l =>
        {
            var order = orders[l.DocumentId!.Value];
            return new OpenLine(l, order, Route(l.ElementId!.Value, l.FromId ?? order.FromId));
        })];
    }

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "Production points belong to a company: choose one of its nodes.");
}

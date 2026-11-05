using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Stock;

/// <summary>An ingredient of a run: how much goes out of stock (waste included) and how much of that is waste; with what the place holds.</summary>
public sealed record ProductionLineDto(Guid ProductId, string? Name, Guid UnitId, decimal Quantity, decimal WasteQuantity, decimal? Held = null);

/// <summary>
/// Make a quantity of a product: the raw materials out of a place, the product into the same place or <see cref="ToCostCenterId"/>, on a day,
/// perhaps for a customer's order; with the ingredients as the recipe scales them, or as actually used (<see cref="Lines"/>). The production
/// document type is the company's only one unless <see cref="TypeId"/> names it.
/// </summary>
public sealed record ProduceRequest(Guid CostCenterId, Guid ProductId, Guid UnitId, decimal Quantity, DateOnly? Date = null, Guid? OrderId = null, string? Note = null,
    IReadOnlyList<ProductionLineDto>? Lines = null, Guid? ToCostCenterId = null, Guid? TypeId = null, bool DeliverAnywhereInCompany = false);

/// <summary>An ingredient that comes back when a product is taken apart: how much comes back and how much is lost; with what the place holds.</summary>
public sealed record DisassemblyLineDto(Guid ProductId, string? Name, Guid UnitId, decimal Recovered, decimal Lost, decimal? Held = null);

/// <summary>
/// Take a quantity of a product apart at a place: the product out, its ingredients back into the same place or <see cref="ToCostCenterId"/>, as
/// the recipe's recovered % gives them or as given (<see cref="Lines"/>). The disassembly document type is the company's only one unless
/// <see cref="TypeId"/> names it.
/// </summary>
public sealed record DisassembleRequest(Guid CostCenterId, Guid ProductId, Guid UnitId, decimal Quantity, DateOnly? Date = null, string? Note = null,
    IReadOnlyList<DisassemblyLineDto>? Lines = null, Guid? ToCostCenterId = null, Guid? TypeId = null);

/// <summary>A customer's order with products that have a recipe and no production yet.</summary>
public sealed record OrderToProduceDto(Guid Id, string? Code, DateOnly? Date, string? Client, IReadOnlyList<ProductionLineDto> Products);

/// <summary>
/// Production as a financial document (the founder's rule,
/// 2026-10-30): a "Production" document takes the raw materials out of their place at what they cost (first in, first out), puts the product
/// into its place at that cost and posts both through the production account, so the books and the stock's value move together; the waste
/// (the recipe's waste %, or as given) is a line of its own, out of stock to the waste expense account and not part of the product's cost. One level deep: an ingredient made here is produced first.
/// </summary>
public sealed class ProductionService(ErpDbContext db, RequestContext request, Visibility visibility, TimeProvider clock)
{
    private readonly StockDocuments _documents = new(db, request, visibility, clock);

    // The stock directions of a production's line types: what is used, what is made, what is lost.
    private const int Raw = 2, Produced = 3, Waste = 4;

    /// <summary>The stock direction of a line that makes a product.</summary>
    private const int MadeDirection = Produced;

    // A disassembly's: the product taken apart goes out (2), what comes back comes in (3), what is lost carries only its value (5).
    private const int TakenApart = 2, Recovered = 3, Lost = Posting.MovementService.ValueOnly;

    /// <summary>The ingredients a quantity of the product takes, scaled from its recipe, with what the place holds of each.</summary>
    public async Task<IReadOnlyList<ProductionLineDto>> CalculateAsync(Guid productId, Guid unitId, decimal quantity, Guid? costCenter, CancellationToken ct)
    {
        var lines = await RecipeLinesAsync(productId, unitId, quantity, ct).ConfigureAwait(false);
        if (costCenter is null)
        {
            return lines;
        }

        await _documents.CompanyOfAsync(costCenter.Value, "costCenter", ct).ConfigureAwait(false);
        var held = new List<ProductionLineDto>();
        foreach (var line in lines)
        {
            held.Add(line with { Held = await _documents.HeldAsync(costCenter.Value, line.ProductId, line.UnitId, ct).ConfigureAwait(false) });
        }

        return held;
    }

    /// <summary>Makes and posts the production document.</summary>
    public async Task<DocumentDto> ProduceAsync(ProduceRequest produce, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(produce);
        if (produce.Quantity <= 0)
        {
            throw ValidationException.For("quantity", "The quantity is above 0.");
        }

        var wanted = produce.Lines is { Count: > 0 } given ? given : await RecipeLinesAsync(produce.ProductId, produce.UnitId, produce.Quantity, ct).ConfigureAwait(false);
        if (wanted.Count == 0)
        {
            throw ValidationException.For("productId", $"{ErrorCodes.NotConfiguredError}: the product has no recipe for this unit; give the ingredients used.");
        }

        var errors = wanted.Select((l, i) => (l, i)).Where(x => x.l.Quantity <= 0 || x.l.WasteQuantity < 0 || x.l.WasteQuantity > x.l.Quantity || x.l.ProductId == produce.ProductId)
            .ToDictionary(x => $"lines[{x.i}]", _ => "An ingredient is another product, used in a quantity above 0, of which the waste is a part.");
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return await _documents.PostAsync(StockDocuments.Production, produce.TypeId, produce.CostCenterId, produce.ToCostCenterId ?? produce.CostCenterId, produce.Date,
            [.. wanted.Where(l => l.Quantity > l.WasteQuantity).Select(l => new StockDocumentLine(Raw, l.ProductId, l.UnitId, l.Quantity - l.WasteQuantity)),
                .. wanted.Where(l => l.WasteQuantity > 0).Select(l => new StockDocumentLine(Waste, l.ProductId, l.UnitId, l.WasteQuantity)),
                new StockDocumentLine(Produced, produce.ProductId, produce.UnitId, produce.Quantity)],
            produce.OrderId is { } order ? [order] : [], produce.Note, ct, produce.DeliverAnywhereInCompany).ConfigureAwait(false);
    }

    /// <summary>The company's document types of a kind (production, disassembly, unit split), for the screens to choose one when there are several.</summary>
    public async Task<IReadOnlyList<Guid>> TypesOfAsync(string kind, CancellationToken ct)
    {
        var company = await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("kind", "Choose a company first.");
        var layers = kind switch
        {
            "production" => StockDocuments.Production,
            "disassembly" => StockDocuments.Disassembly,
            "unit-split" => StockDocuments.UnitSplit,
            _ => throw ValidationException.For("kind", "The kind is production, disassembly or unit-split."),
        };
        return await _documents.TypesAsync(company, layers, ct).ConfigureAwait(false);
    }

    /// <summary>What a place holds of a product in a unit.</summary>
    public async Task<decimal> HeldAsync(Guid costCenter, Guid product, Guid unit, CancellationToken ct)
    {
        await _documents.CompanyOfAsync(costCenter, "costCenter", ct).ConfigureAwait(false);
        return await _documents.HeldAsync(costCenter, product, unit, ct).ConfigureAwait(false);
    }

    /// <summary>The document types an order to produce can be: the top-level types whose settings list their orders on the Production screen.</summary>
    public async Task<IReadOnlyList<Guid>> OrderTypesAsync(CancellationToken ct) =>
        await db.Set<DocumentType>().AsNoTracking().Where(t => t.Active && t.ProducedOnProductionScreen && t.IdSup == null).Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false);

    /// <summary>
    /// The orders of a type with products that have a recipe and are not all produced yet, each with
    /// what is left of it (the founder, 2026-11-04): the ordered quantity less what the active production documents made from the order made of that
    /// product and unit (their lines of the "made" stock direction). An order leaves the list when nothing is left.
    /// </summary>
    public async Task<IReadOnlyList<OrderToProduceDto>> OrdersToProduceAsync(Guid orderTypeId, CancellationToken ct)
    {
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var productionTypes = new List<Guid>();
        foreach (var company in companies)
        {
            productionTypes.AddRange(await _documents.TypesAsync(company, StockDocuments.Production, ct).ConfigureAwait(false));
        }

        var orders = await db.Set<Document>().AsNoTracking()
            .Where(d => d.Active && d.TypeId == orderTypeId && companies.Contains(d.CompanyId) && (d.OwnerId == null || nodes.Contains(d.OwnerId.Value)))
            .OrderBy(d => d.DocumentDate).ToListAsync(ct).ConfigureAwait(false);
        var ids = orders.Select(o => o.Id).ToList();
        var made = db.Set<ProductRecipe>().Select(r => r.ProductId);
        var lines = (await db.Set<Transaction>().AsNoTracking()
                .Where(t => t.Active && t.DocumentId != null && ids.Contains(t.DocumentId.Value) && t.ElementId != null && t.UnitId != null && made.Contains(t.ElementId.Value))
                .ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(t => t.DocumentId!.Value);
        // What the order's productions made: their lines of the "made" stock direction (3), by order, product and unit.
        var nullableIds = ids.Select(i => (Guid?)i).ToList();
        var done = (await (from l in db.Set<InterDocumentDocument>().AsNoTracking()
                           join d in db.Set<Document>().AsNoTracking() on l.Document2Id equals d.Id
                           join t in db.Set<Transaction>().AsNoTracking() on d.Id equals t.DocumentId
                           join c in db.Set<TransactionTypeConf>().AsNoTracking() on t.TypeConfId equals c.Id
                           where l.Active && d.Active && t.Active && nullableIds.Contains(l.Document1Id) && productionTypes.Contains(d.TypeId) && c.StockDirection == MadeDirection
                           select new { Order = l.Document1Id!.Value, t.ElementId, t.UnitId, Quantity = t.Quantity ?? 0 }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(x => (x.Order, x.ElementId, x.UnitId)).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        return [.. orders.Select(o => new OrderToProduceDto(o.Id, o.Code, o.DocumentDate, o.ToName,
                [.. lines[o.Id].GroupBy(t => (Product: t.ElementId!.Value, Unit: t.UnitId!.Value, Name: t.ElementName))
                    .Select(g => new ProductionLineDto(g.Key.Product, g.Key.Name, g.Key.Unit, g.Sum(t => t.Quantity ?? 0) - done.GetValueOrDefault((o.Id, g.Key.Product, g.Key.Unit)), 0))
                    .Where(l => l.Quantity > 0)]))
            .Where(o => o.Products.Count > 0)];
    }

    /// <summary>Produces every product of an order that has a recipe, its ordered quantity, at a place: one production document each, all or none.</summary>
    public async Task<IReadOnlyList<DocumentDto>> ProduceOrderAsync(Guid orderId, Guid costCenterId, DateOnly? date, CancellationToken ct)
    {
        var order = await db.Set<Document>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == orderId, ct).ConfigureAwait(false);
        var toProduce = order is null ? null : (await OrdersToProduceAsync(order.TypeId, ct).ConfigureAwait(false)).FirstOrDefault(o => o.Id == orderId);
        if (toProduce is null)
        {
            throw ValidationException.For("orderId", "The order has nothing left to produce.");
        }

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        var made = new List<DocumentDto>();
        foreach (var line in toProduce.Products)
        {
            made.Add(await ProduceAsync(new ProduceRequest(costCenterId, line.ProductId, line.UnitId, line.Quantity, date, orderId, toProduce.Code), ct).ConfigureAwait(false));
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return made;
    }

    /// <summary>What taking a quantity of the product apart gives back, from its recipe (no making waste: the product holds the recipe's quantity), with what the place holds.</summary>
    public async Task<IReadOnlyList<DisassemblyLineDto>> CalculateDisassemblyAsync(Guid productId, Guid unitId, decimal quantity, Guid? costCenter, CancellationToken ct)
    {
        var lines = await RecipeRowsAsync(productId, unitId, ct).ConfigureAwait(false);
        var ids = lines.Select(r => r.ComponentId).Distinct().ToList();
        var names = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name.In(TranslatedText.DefaultLanguage), ct)
            .ConfigureAwait(false);
        if (costCenter is not null)
        {
            await _documents.CompanyOfAsync(costCenter.Value, "costCenter", ct).ConfigureAwait(false);
        }

        var result = new List<DisassemblyLineDto>();
        foreach (var r in lines)
        {
            var whole = quantity * r.Quantity;
            var back = Math.Round(whole * r.RecoveredPercent / 100, 6, MidpointRounding.AwayFromZero);
            result.Add(new DisassemblyLineDto(r.ComponentId, names.GetValueOrDefault(r.ComponentId), r.ComponentUnitId, back, whole - back,
                costCenter is { } place ? await _documents.HeldAsync(place, r.ComponentId, r.ComponentUnitId, ct).ConfigureAwait(false) : null));
        }

        return result;
    }

    /// <summary>
    /// Makes and posts the disassembly document: the product out of its place at what it cost (first in, first out), what comes back into its place
    /// and what is lost to the waste expense, sharing that cost by quantity times each ingredient's average cost. It is not reversed.
    /// </summary>
    public async Task<DocumentDto> DisassembleAsync(DisassembleRequest take, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(take);
        if (take.Quantity <= 0)
        {
            throw ValidationException.For("quantity", "The quantity is above 0.");
        }

        var lines = take.Lines is { Count: > 0 } given ? given : await CalculateDisassemblyAsync(take.ProductId, take.UnitId, take.Quantity, null, ct).ConfigureAwait(false);
        if (lines.Count == 0)
        {
            throw ValidationException.For("productId", $"{ErrorCodes.NotConfiguredError}: the product has no recipe for this unit; give what comes back.");
        }

        var errors = lines.Select((l, i) => (l, i)).Where(x => x.l.Recovered < 0 || x.l.Lost < 0 || x.l.Recovered + x.l.Lost <= 0 || x.l.ProductId == take.ProductId)
            .ToDictionary(x => $"lines[{x.i}]", _ => "An ingredient is another product; what comes back and what is lost are 0 or more, and not both 0.");
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return await _documents.PostAsync(StockDocuments.Disassembly, take.TypeId, take.CostCenterId, take.ToCostCenterId ?? take.CostCenterId, take.Date,
            [new StockDocumentLine(TakenApart, take.ProductId, take.UnitId, take.Quantity),
                .. lines.Where(l => l.Recovered > 0).Select(l => new StockDocumentLine(Recovered, l.ProductId, l.UnitId, l.Recovered)),
                .. lines.Where(l => l.Lost > 0).Select(l => new StockDocumentLine(Lost, l.ProductId, l.UnitId, l.Lost))],
            [], take.Note, ct).ConfigureAwait(false);
    }

    /// <summary>The recipe's rows for the unit, or else those for any unit.</summary>
    private async Task<List<ProductRecipe>> RecipeRowsAsync(Guid productId, Guid unitId, CancellationToken ct)
    {
        var rows = await db.Set<ProductRecipe>().AsNoTracking().Where(r => r.ProductId == productId && r.Quantity > 0 && (r.UnitId == unitId || r.UnitId == null))
            .OrderBy(r => r.SortOrder).ToListAsync(ct).ConfigureAwait(false);
        return rows.Any(r => r.UnitId == unitId) ? [.. rows.Where(r => r.UnitId == unitId)] : rows;
    }

    /// <summary>The recipe scaled to a quantity: the rows for the unit, or else those for any unit; waste on top of each.</summary>
    private async Task<List<ProductionLineDto>> RecipeLinesAsync(Guid productId, Guid unitId, decimal quantity, CancellationToken ct)
    {
        var rows = await db.Set<ProductRecipe>().AsNoTracking().Where(r => r.ProductId == productId && r.Quantity > 0 && (r.UnitId == unitId || r.UnitId == null))
            .OrderBy(r => r.SortOrder).ToListAsync(ct).ConfigureAwait(false);
        var used = rows.Any(r => r.UnitId == unitId) ? rows.Where(r => r.UnitId == unitId).ToList() : rows;
        var ids = used.Select(r => r.ComponentId).Distinct().ToList();
        var names = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name.In(TranslatedText.DefaultLanguage), ct)
            .ConfigureAwait(false);
        return [.. used.Select(r =>
        {
            var net = quantity * r.Quantity;
            var waste = net * r.WastePercent / 100;
            return new ProductionLineDto(r.ComponentId, names.GetValueOrDefault(r.ComponentId), r.ComponentUnitId, net + waste, waste);
        })];
    }
}

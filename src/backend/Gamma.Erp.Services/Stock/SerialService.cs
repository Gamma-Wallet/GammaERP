using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Employees;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Stock;

/// <summary>A serial number and who holds it now.</summary>
public sealed record SerialRowDto(Guid Id, Guid ProductId, string? ProductCode, string? Product, string Serial, string? PartNumber, Guid? HolderId, string? Holder);

/// <summary>One move of a serial number: the posted document that moved it, from where to where.</summary>
public sealed record SerialMoveDto(DateOnly? PostingDate, Guid DocumentId, string? Document, string? DocumentType, string? From, string? To);

/// <summary>A tracked product at a place: how much stock it holds and how many of those have their serial number.</summary>
public sealed record SerialStockDto(Guid CostCenterId, string? CostCenter, Guid ProductId, string? ProductCode, string? Product, Guid UnitId, string? Unit, decimal Quantity, int Serials);

/// <summary>Serial numbers given to stock already held.</summary>
public sealed record AssignSerialsRequest(Guid CostCenterId, Guid ProductId, Guid UnitId, IReadOnlyList<TransactionSerialDto> Serials);

/// <summary>
/// The serial register: who holds each unit of a product that tracks serial numbers, the moves it made, and serial numbers given to stock already
/// held. The stock itself does not change: the units stay under their product.
/// </summary>
public sealed class SerialService(ErpDbContext db, RequestContext request, Visibility visibility)
{
    /// <summary>The serial numbers the company holds, or one holder's, one product's, matching a search.</summary>
    public async Task<IReadOnlyList<SerialRowDto>> SerialsAsync(Guid? productId, Guid? holderId, string? search, string language, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var term = search?.Trim();
        var rows = await db.Set<ProductSerial>().AsNoTracking().Where(s => s.CompanyId == company && (productId == null || s.ProductId == productId) && (holderId == null || s.HolderId == holderId)
                && (string.IsNullOrEmpty(term) || EF.Functions.ILike(s.Serial, $"%{term}%") || (s.PartNumber != null && EF.Functions.ILike(s.PartNumber, $"%{term}%"))))
            .OrderBy(s => s.Serial).Take(500).ToListAsync(ct).ConfigureAwait(false);
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        var holders = await HoldersAsync(rows.Select(r => r.HolderId).OfType<Guid>().Distinct().ToList(), language, ct).ConfigureAwait(false);
        return [.. rows.Select(r =>
        {
            var product = products.GetValueOrDefault(r.ProductId);
            return new SerialRowDto(r.Id, r.ProductId, product?.Code, product?.Name.In(language), r.Serial, r.PartNumber, r.HolderId, r.HolderId is { } holder ? holders.GetValueOrDefault(holder) : null);
        })];
    }

    /// <summary>Every posted move of a serial number, oldest first.</summary>
    public async Task<IReadOnlyList<SerialMoveDto>> MovesAsync(Guid serialId, string language, CancellationToken ct)
    {
        var moves = await (from s in db.Set<TransactionSerial>().AsNoTracking()
                           join t in db.Set<Transaction>().AsNoTracking() on s.TransactionId equals t.Id
                           join d in db.Set<Document>().AsNoTracking() on t.DocumentId equals d.Id
                           join k in db.Set<DocumentType>().AsNoTracking() on d.TypeId equals k.Id
                           where s.ProductSerialId == serialId && d.Posted && !k.NeverPosted
                           orderby d.PostingDate, d.Id
                           select new { d.PostingDate, d.Id, d.Code, Type = k.Name, d.FromName, d.ToName }).ToListAsync(ct).ConfigureAwait(false);
        return [.. moves.Select(m => new SerialMoveDto(m.PostingDate, m.Id, m.Code, m.Type.In(language), m.FromName, m.ToName))];
    }

    /// <summary>The tracked products the company's places hold, with how many of them have their serial number.</summary>
    public async Task<IReadOnlyList<SerialStockDto>> StockAsync(string language, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var stock = await (from s in db.Set<InterCostCenterProductStock>().AsNoTracking()
                           join u in db.Set<InterProductSpecificUnit>().AsNoTracking() on s.ProductSpecificUnitId equals u.Id
                           join p in db.Set<Product>().AsNoTracking() on u.ProductId equals p.Id
                           join k in db.Set<ProductType>().AsNoTracking() on p.TypeId equals k.Id
                           where s.CompanyId == company && k.TracksSerial && s.CostCenterId != null && s.Quantity > 0 && u.UnitId != null
                           select new { CostCenterId = s.CostCenterId!.Value, ProductId = p.Id, p.Code, p.Name, UnitId = u.UnitId!.Value, Quantity = s.Quantity ?? 0 }).ToListAsync(ct).ConfigureAwait(false);
        var places = stock.Select(s => s.CostCenterId).Distinct().ToList();
        var serials = (await db.Set<ProductSerial>().AsNoTracking().Where(s => s.CompanyId == company && s.HolderId != null && places.Contains(s.HolderId.Value))
                .GroupBy(s => new { s.HolderId, s.ProductId }).Select(g => new { g.Key.HolderId, g.Key.ProductId, Count = g.Count() }).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(s => (s.HolderId!.Value, s.ProductId), s => s.Count);
        var names = await db.Set<CostCenter>().AsNoTracking().Where(c => places.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name.In(language), ct).ConfigureAwait(false);
        var unitIds = stock.Select(s => s.UnitId).Distinct().ToList();
        var units = await db.Set<Models.Units.Unit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name.In(language), ct).ConfigureAwait(false);
        return [.. stock.Select(s => new SerialStockDto(s.CostCenterId, names.GetValueOrDefault(s.CostCenterId), s.ProductId, s.Code, s.Name.In(language), s.UnitId, units.GetValueOrDefault(s.UnitId),
            s.Quantity, serials.GetValueOrDefault((s.CostCenterId, s.ProductId)))).OrderBy(s => s.CostCenter).ThenBy(s => s.Product)];
    }

    /// <summary>
    /// Gives serial numbers to stock a place already holds: never more than it holds, each once.
    /// The stock and its cost stay as they are.
    /// </summary>
    public async Task<IReadOnlyList<SerialRowDto>> AssignAsync(AssignSerialsRequest assign, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(assign);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var tracks = await (from p in db.Set<Product>().AsNoTracking() join k in db.Set<ProductType>().AsNoTracking() on p.TypeId equals k.Id
                            where p.Id == assign.ProductId select (bool?)k.TracksSerial).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (tracks != true)
        {
            throw ValidationException.For("productId", "This product does not track serial numbers: set it on its type first.");
        }

        var given = assign.Serials.Where(s => !string.IsNullOrWhiteSpace(s.Serial)).Select(s => s with { Serial = s.Serial.Trim(), PartNumber = string.IsNullOrWhiteSpace(s.PartNumber) ? null : s.PartNumber.Trim() }).ToList();
        if (given.Count == 0 || given.Select(s => s.Serial).Distinct(StringComparer.OrdinalIgnoreCase).Count() != given.Count)
        {
            throw ValidationException.For("serials", "Give each serial number once.");
        }

        var held = await (from s in db.Set<InterCostCenterProductStock>().AsNoTracking()
                          join u in db.Set<InterProductSpecificUnit>().AsNoTracking() on s.ProductSpecificUnitId equals u.Id
                          where s.CompanyId == company && s.CostCenterId == assign.CostCenterId && u.ProductId == assign.ProductId && u.UnitId == assign.UnitId
                          select s.Quantity ?? 0).SumAsync(ct).ConfigureAwait(false);
        var numbered = await db.Set<ProductSerial>().AsNoTracking().CountAsync(s => s.CompanyId == company && s.ProductId == assign.ProductId && s.HolderId == assign.CostCenterId, ct).ConfigureAwait(false);
        if (numbered + given.Count > held)
        {
            throw ValidationException.For("serials", $"The place holds {held:0.###}, of which {numbered} have a serial number: {given.Count} more do not fit.");
        }

        var codes = given.Select(s => s.Serial).ToList();
        if (await db.Set<ProductSerial>().AsNoTracking().Where(s => s.CompanyId == company && s.ProductId == assign.ProductId && codes.Contains(s.Serial)).Select(s => s.Serial)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false) is { } taken)
        {
            throw ValidationException.For("serials", $"Serial number {taken} already exists.");
        }

        var table = db.Model.FindEntityType(typeof(CostCenter))!.GetTableName();
        db.AddRange(given.Select(s => new ProductSerial { CompanyId = company, ProductId = assign.ProductId, Serial = s.Serial, PartNumber = s.PartNumber, HolderTable = table, HolderId = assign.CostCenterId,
            TenantId = request.TenantId, OwnerId = company }));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await SerialsAsync(assign.ProductId, assign.CostCenterId, null, language, ct).ConfigureAwait(false);
    }

    private async Task<Dictionary<Guid, string?>> HoldersAsync(List<Guid> ids, string language, CancellationToken ct)
    {
        var names = await db.Set<CostCenter>().AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name.In(language), ct).ConfigureAwait(false);
        foreach (var (id, name) in await db.Set<Partner>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name.In(language), ct).ConfigureAwait(false))
        {
            names[id] = name;
        }

        foreach (var (id, name) in await db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name.In(language), ct).ConfigureAwait(false))
        {
            names[id] = name;
        }

        return names;
    }

    private async Task<Guid> CompanyAsync(CancellationToken ct) =>
        await visibility.CompanyAsync(ct).ConfigureAwait(false) ?? throw ValidationException.For("company", "Serial numbers belong to a company: choose one of its nodes.");
}

using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Posting;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Stock;

/// <summary>
/// One line of a production or a unit split, by the stock direction of its line type: 2 a raw material used (out of the from place), 3 a product
/// made (into the to place), 4 waste (out of the from place, an expense).
/// </summary>
public sealed record StockDocumentLine(int Direction, Guid ProductId, Guid UnitId, decimal Quantity);

/// <summary>
/// Production and unit splits are financial documents (the founder's rule, 2026-10-30): this makes one and posts it, through the document
/// service, so it is numbered, checked (the company's negative-stock setting, what the company holds), valued (first in, first out) and posted
/// to the books like any other. The document type is found by its configuration, never by name: the company's type whose to side makes a
/// layer at what the raw materials cost (a production) or receives them converted (a unit split); its raw-material and produced line types are
/// the ones linked to it that move stock out of the from side only and into the to side only.
/// </summary>
internal sealed class StockDocuments(ErpDbContext db, RequestContext request, Visibility visibility, TimeProvider clock)
{
    /// <summary>
    /// The company a place is in: itself or the nearest node above it whose type keeps its own books. The place must be one the request sees, unless
    /// <paramref name="anywhereInCompany"/>: then any place of a company the request sees (a production point delivering to the point of sale that ordered).
    /// </summary>
    public async Task<Guid> CompanyOfAsync(Guid place, string field, CancellationToken ct, bool anywhereInCompany = false)
    {
        if (!anywhereInCompany && !(await visibility.NodesAsync(ct).ConfigureAwait(false)).Contains(place))
        {
            throw ValidationException.For(field, "The place does not exist.");
        }

        var parents = await db.Set<CostCenter>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.IdSup, ct).ConfigureAwait(false);
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        for (Guid? node = place; node is { } n && parents.ContainsKey(n); node = parents[n])
        {
            if (companies.Contains(n))
            {
                return n;
            }
        }

        throw ValidationException.For(field, "The place is in no company.");
    }

    /// <summary>The company's document types whose to side does <paramref name="toLayers"/> (not their reversals).</summary>
    public async Task<List<Guid>> TypesAsync(Guid company, int toLayers, CancellationToken ct) =>
        await (from t in db.Set<DocumentType>().AsNoTracking()
               join c in db.Set<DocumentTypeConfTo>().AsNoTracking() on t.Id equals c.DocumentTypeId
               where t.OwnerId == company && !t.IsReversal && c.CostLayers == toLayers
               select t.Id).Distinct().ToListAsync(ct).ConfigureAwait(false);

    /// <summary>Makes the document, from one place to another (or the same), and posts it; all or nothing.</summary>
    public async Task<DocumentDto> PostAsync(int toLayers, Guid? typeId, Guid from, Guid to, DateOnly? date, IReadOnlyList<StockDocumentLine> lines,
        IReadOnlyList<Guid> createdFrom, string? note, CancellationToken ct, bool toAnywhereInCompany = false)
    {
        var company = await CompanyOfAsync(from, "costCenterId", ct).ConfigureAwait(false);
        if (await CompanyOfAsync(to, "toCostCenterId", ct, toAnywhereInCompany).ConfigureAwait(false) != company)
        {
            throw ValidationException.For("toCostCenterId", "Both places are in the same company.");
        }

        var types = await TypesAsync(company, toLayers, ct).ConfigureAwait(false);
        var type = typeId is { } given ? types.Contains(given) ? given : throw ValidationException.For("typeId", "The document type is not one for this.")
            : types.Count == 1 ? types[0] : throw ValidationException.For("typeId", $"{ErrorCodes.NotConfiguredError}: the company has {types.Count} document types for this; choose one.");
        var kinds = await (from l in db.Set<InterDocumentTypeTransactionType>().AsNoTracking()
                           join c in db.Set<TransactionTypeConf>().AsNoTracking() on l.TransactionTypeId equals c.TransactionTypeId
                           where l.DocumentTypeId == type && c.StockDirection >= 2 && c.StockDirection <= Posting.MovementService.ValueOnly
                           select new { c.StockDirection, l.TransactionTypeId }).ToListAsync(ct).ConfigureAwait(false);
        Guid Kind(int direction) => kinds.FirstOrDefault(k => k.StockDirection == direction)?.TransactionTypeId
            ?? throw ValidationException.For("typeId", $"{ErrorCodes.NotConfiguredError}: the document type has no line type for " +
                $"{direction switch { 3 => "products made", 4 => "waste", Posting.MovementService.ValueOnly => "what is lost", _ => "raw materials used" }}.");
        var vat = await db.Set<InterDocumentTypeVatGroup>().AsNoTracking().Where(v => v.DocumentTypeId == type).Select(v => v.VatGroupId).FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var categories = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(c => c.TypeId == type).Select(c => c.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        if (products.Count != ids.Count)
        {
            throw ValidationException.For("productId", "A product does not exist.");
        }

        var places = await db.Set<CostCenter>().AsNoTracking().Where(c => c.Id == from || c.Id == to).ToDictionaryAsync(c => c.Id, c => c.Name.In(TranslatedText.DefaultLanguage), ct)
            .ConfigureAwait(false);
        var day = date ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var documents = new DocumentService(db, request, visibility, clock);
        var dto = new DocumentDto
        {
            TypeId = type, CategoryIds = categories, DocumentDate = day, PostingDate = day, FromId = from,
            FromName = places.GetValueOrDefault(from), ToId = to, ToName = places.GetValueOrDefault(to), CreatedFromIds = createdFrom,
            Description = string.IsNullOrWhiteSpace(note) ? null : new Dictionary<string, string> { [TranslatedText.DefaultLanguage] = note.Trim() },
            Transactions = [.. lines.Select(l => new TransactionDto
            {
                TypeId = Kind(l.Direction), ElementId = l.ProductId, ElementTypeId = products[l.ProductId].TypeId, ElementName = products[l.ProductId].Name.In(TranslatedText.DefaultLanguage),
                UnitId = l.UnitId, Quantity = l.Quantity, Price = 0, VatGroupId = vat,
            })],
        };
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        var saved = await documents.SaveAsync(dto, ct).ConfigureAwait(false);
        var posted = await documents.PostAsync(saved.Id!.Value, ct).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return posted;
    }

    /// <summary>What a place holds of a product in a unit.</summary>
    public async Task<decimal> HeldAsync(Guid place, Guid product, Guid unit, CancellationToken ct) =>
        await (from s in db.Set<InterCostCenterProductStock>().AsNoTracking()
               join u in db.Set<InterProductSpecificUnit>().AsNoTracking() on s.ProductSpecificUnitId equals u.Id
               where s.CostCenterId == place && u.ProductId == product && u.UnitId == unit
               select s.Quantity ?? 0).SumAsync(ct).ConfigureAwait(false);

    /// <summary>The cost-layer mode of a production's to side (it makes the product at what was used).</summary>
    public const int Production = CostLayerBook.MakeFromUsed;

    /// <summary>The cost-layer mode of a unit split's to side (it receives what was taken, converted).</summary>
    public const int UnitSplit = CostLayerBook.ReceiveConverted;

    /// <summary>The cost-layer mode of a disassembly's to side (it receives what was taken, shared by average cost).</summary>
    public const int Disassembly = CostLayerBook.ShareTaken;
}

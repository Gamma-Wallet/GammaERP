using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.Depreciation;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Depreciation;

/// <summary>
/// Selling a fixed asset is selling its product (the founder, 2026-11-02): the sale line proposes the asset's value in the book that posts, each posted line carries the asset's
/// cost, depreciation and book value for the scenarios to read, and the asset leaves depreciation until a reversal brings it back.
/// </summary>
public sealed class AssetSale(ErpDbContext db)
{
    /// <summary>What is left of the product's asset in the book that posts; null when it is no asset there, or is sold.</summary>
    public async Task<decimal?> RemainingValueAsync(Guid companyId, Guid productId, Guid? unitId, CancellationToken ct)
    {
        var values = (await ValuesAsync(companyId, productId, unitId, ct).ConfigureAwait(false)).Where(v => v.SoldOn is null).ToList();
        return values.Count == 0 ? null : values.Sum(v => v.InitialValue ?? 0) - await DepreciatedAsync(values, ct).ConfigureAwait(false);
    }

    /// <summary>Gives each line of a sale, or of its reversal, the figures of the asset it sells.</summary>
    public async Task FillAsync(Document document, IReadOnlyList<Transaction> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(lines);
        if (await SaleAsync(document, ct).ConfigureAwait(false) is not { } reversal)
        {
            return;
        }

        foreach (var line in lines.Where(l => l.ElementId is not null))
        {
            var values = (await ValuesAsync(document.CompanyId, line.ElementId!.Value, line.UnitId, ct).ConfigureAwait(false)).Where(v => reversal == v.SoldOn is not null).ToList();
            if (values.Count > 0)
            {
                line.AssetCostLcu = values.Sum(v => v.InitialValue ?? 0);
                line.AssetDepreciatedLcu = await DepreciatedAsync(values, ct).ConfigureAwait(false);
                line.AssetBookValueLcu = line.AssetCostLcu - line.AssetDepreciatedLcu;
            }
        }
    }

    /// <summary>A posted sale takes its assets out of depreciation in every book, the tax book too; its reversal brings them back.</summary>
    public async Task CloseAsync(Document document, IReadOnlyList<Transaction> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(lines);
        if (await SaleAsync(document, ct).ConfigureAwait(false) is not { } reversal)
        {
            return;
        }

        foreach (var line in lines.Where(l => l.ElementId is not null && l.AssetBookValueLcu is not null))
        {
            var sold = (await ValuesAsync(document.CompanyId, line.ElementId!.Value, line.UnitId, ct, tracked: true, everyBook: true).ConfigureAwait(false))
                .Where(v => reversal ? v.SoldDocumentId == document.ReversedDocumentId : v.SoldOn is null);
            foreach (var value in sold)
            {
                value.SoldOn = reversal ? null : document.PostingDate;
                value.SoldDocumentId = reversal ? null : document.Id;
            }
        }
    }

    private async Task<List<AssetValue>> ValuesAsync(Guid companyId, Guid productId, Guid? unitId, CancellationToken ct, bool tracked = false, bool everyBook = false)
    {
        var specific = db.Set<InterProductSpecificUnit>().Where(s => s.ProductId == productId && (unitId == null || s.UnitId == unitId)).Select(s => (Guid?)s.Id);
        var posting = db.Set<DepreciationBook>().Where(b => b.Posts).Select(b => (Guid?)b.Id);
        var values = db.Set<AssetValue>().Where(v => v.CompanyId == companyId && specific.Contains(v.ProductSpecificUnitId) && (everyBook || posting.Contains(v.BookId)));
        return await (tracked ? values : values.AsNoTracking()).ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<decimal> DepreciatedAsync(List<AssetValue> values, CancellationToken ct)
    {
        var ids = values.Select(v => (Guid?)v.Id).ToList();
        return await db.Set<AssetDepreciation>().AsNoTracking().Where(d => ids.Contains(d.AssetValueId)).SumAsync(d => d.DepreciationValue ?? 0, ct).ConfigureAwait(false);
    }

    /// <summary>Whether a document type sells the assets on its lines (configuration).</summary>
    public Task<bool> SellsAssetsAsync(Guid typeId, CancellationToken ct) => db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == typeId && t.SellsAssets, ct);

    // The type's configuration tells a sale: a transfer to a client has the partner on its "to" side too. True: its reversal; false: the sale; null: neither.
    private async Task<bool?> SaleAsync(Document document, CancellationToken ct)
    {
        var type = await db.Set<DocumentType>().AsNoTracking().FirstAsync(t => t.Id == document.TypeId, ct).ConfigureAwait(false);
        var sold = type.ReversedDocumentTypeId ?? type.Id;
        var sale = await db.Set<DocumentType>().AsNoTracking().AnyAsync(t => t.Id == sold && t.SellsAssets, ct).ConfigureAwait(false);
        return sale ? type.ReversedDocumentTypeId is not null : null;
    }
}

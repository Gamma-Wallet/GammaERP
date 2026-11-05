using Gamma.Erp.Models.Base;
using Gamma.Erp.Models.CostCenters;
using Gamma.Erp.Models.Currencies;
using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Documents.Dto;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Payments;
using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Products;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Models.Transactions.Dto;
using Gamma.Erp.Models.Units;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Gamma.Erp.Services.Posting;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

/// <summary>
/// A sale from the group is the company's purchase (the founder, 2026-10-31): posting it makes, in the same database transaction, an unposted
/// purchase in the company's books, for the company to check and post. The books share no rows, so everything on a line is found again in the
/// company's own by its code (a VAT group by its rate). Which sale makes which purchase is <see cref="GroupPurchaseSetting"/>. What the company
/// has no match for stays empty on the draft, for the company to choose (so does an exchange rate it has none of for the day); a posting checks
/// every line again, so it can't be posted like that.
/// </summary>
internal sealed class GroupPurchaseService(ErpDbContext db, RequestContext request, TimeProvider clock)
{
    /// <summary>Makes the company's draft when the sale is one its settings say makes a purchase; other documents pass through untouched.</summary>
    public async Task MakeDraftAsync(Document sale, IReadOnlyList<Transaction> lines, CancellationToken ct)
    {
        if (await CompanyOfAsync(sale, ct).ConfigureAwait(false) is not { } company
            || await db.Set<GroupPurchaseSetting>().AsNoTracking().Where(s => s.SaleTypeId == sale.TypeId && s.Code != null).Select(s => s.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false) is not { } pair
            || await db.Set<GroupPurchaseSetting>().AsNoTracking().FirstOrDefaultAsync(s => s.OwnerId == company && s.Code == pair && s.PurchaseTypeId != null, ct).ConfigureAwait(false) is not { } setting)
        {
            return;
        }

        var supplier = await db.Set<Partner>().AsNoTracking().FirstOrDefaultAsync(p => p.OwnerId == company && p.GroupMemberId == sale.CompanyId, ct).ConfigureAwait(false)
            ?? throw ValidationException.For("toId", $"{ErrorCodes.NotConfiguredError}: the company has no partner for the group.");
        var purchaseType = setting.PurchaseTypeId!.Value;
        var (service, visibility) = Acting(company);
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var warehouse = setting.WarehouseId is { } w ? await db.Set<CostCenter>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == w, ct).ConfigureAwait(false) : null;
        var categories = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => l.TypeId == purchaseType).Select(l => l.CategoryId).ToListAsync(ct).ConfigureAwait(false);
        var language = TranslatedText.DefaultLanguage;
        await service.SaveForCompanyAsync(new DocumentDto
        {
            TypeId = purchaseType, CategoryIds = categories, PostingDate = sale.PostingDate, DocumentDate = sale.DocumentDate ?? sale.PostingDate,
            FromId = supplier.Id, FromName = supplier.Name.In(language), ToId = warehouse?.Id, ToName = warehouse?.Name.In(language), ExternalNumber = sale.Code,
            Transactions = await LinesAsync(lines, company, purchaseType, nodes, ct).ConfigureAwait(false),
        }, sale.Id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// After a reversal of the sale: the draft the company has not posted goes, and what is left of the sale (a part reversed) is made again.
    /// A purchase the company has posted is the company's; it reverses it itself.
    /// </summary>
    public async Task AfterReversalAsync(Document sale, IReadOnlyList<Transaction> left, CancellationToken ct)
    {
        if (await CompanyOfAsync(sale, ct).ConfigureAwait(false) is not { } company)
        {
            return;
        }

        var drafts = await db.Set<Document>().AsNoTracking().Where(d => d.GroupSaleId == sale.Id && !d.Posted).Select(d => d.Id).ToListAsync(ct).ConfigureAwait(false);
        if (drafts.Count == 0)
        {
            return;
        }

        var (service, _) = Acting(company);
        foreach (var draft in drafts)
        {
            await service.DeleteAsync(draft, ct).ConfigureAwait(false);
        }

        if (left.Count > 0)
        {
            await MakeDraftAsync(sale, left, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The company of the group the document is sold to: its To is the group's partner for one of the companies directly under it.</summary>
    private async Task<Guid?> CompanyOfAsync(Document sale, CancellationToken ct)
    {
        if (sale.ToId is not { } to
            || await db.Set<Partner>().AsNoTracking().Where(p => p.Id == to && p.OwnerId == sale.CompanyId).Select(p => p.GroupMemberId).FirstOrDefaultAsync(ct).ConfigureAwait(false) is not { } member)
        {
            return null;
        }

        return await db.Set<CostCenter>().AsNoTracking().AnyAsync(c => c.Id == member && c.IdSup == sale.CompanyId, ct).ConfigureAwait(false) ? member : null;
    }

    /// <summary>The documents of the company, acted on by the system: the setting decides this, not the rights of the group's user over the company's books.</summary>
    private (DocumentService Service, Visibility Visibility) Acting(Guid company)
    {
        var acting = request with { NodeId = company, IsAdministrator = true };
        var visibility = new Visibility(db, acting);
        return (new DocumentService(db, acting, visibility, clock), visibility);
    }

    private async Task<List<TransactionDto>> LinesAsync(IReadOnlyList<Transaction> lines, Guid company, Guid purchaseType, HashSet<Guid> nodes, CancellationToken ct)
    {
        var language = TranslatedText.DefaultLanguage;
        var typeIds = lines.Select(l => l.TypeId).Distinct().ToList();
        var typeCodes = await db.Set<TransactionType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Code, ct).ConfigureAwait(false);
        var theirTypes = await db.Set<TransactionType>().AsNoTracking().Where(t => t.OwnerId == company && t.Code != null).ToDictionaryAsync(t => t.Code!, t => t.Id, ct).ConfigureAwait(false);
        var confs = (await (from c in db.Set<TransactionTypeConf>().AsNoTracking() join e in db.Set<Elements>().AsNoTracking() on c.ElementsId equals e.Id
                            where c.OwnerId == company select new { c.Id, c.TransactionTypeId, c.StockDirection, Scope = e }).ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(c => c.TransactionTypeId);
        var confIds = lines.Where(l => l.TypeConfId != null).Select(l => l.TypeConfId!.Value).Distinct().ToList();
        var directions = await db.Set<TransactionTypeConf>().AsNoTracking().Where(c => confIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.StockDirection, ct).ConfigureAwait(false);

        var productIds = lines.Where(l => l.ElementId != null).Select(l => l.ElementId!.Value).Distinct().ToList();
        var productCodes = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id) && p.Code != null).ToDictionaryAsync(p => p.Id, p => p.Code!, ct).ConfigureAwait(false);
        var codes = productCodes.Values.ToList();
        var theirProducts = (await db.Set<Product>().AsNoTracking().Where(p => p.OwnerId != null && nodes.Contains(p.OwnerId.Value) && codes.Contains(p.Code!)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(p => p.Code!).ToDictionary(g => g.Key, g => g.First());

        var unitIds = lines.Where(l => l.UnitId != null).Select(l => l.UnitId!.Value).Distinct().ToList();
        var unitCodes = await db.Set<Unit>().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Code, ct).ConfigureAwait(false);
        var theirUnits = await db.Set<Unit>().AsNoTracking().Where(u => u.OwnerId == company && u.Code != null).ToDictionaryAsync(u => u.Code!, u => u.Id, ct).ConfigureAwait(false);

        var vatIds = lines.Where(l => l.VatGroupId != null).Select(l => l.VatGroupId!.Value).Distinct().ToList();
        var vatRates = await db.Set<VatGroup>().AsNoTracking().Where(v => vatIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Rate, ct).ConfigureAwait(false);
        var allowedVat = await db.Set<InterDocumentTypeVatGroup>().AsNoTracking().Where(l => l.DocumentTypeId == purchaseType && l.VatGroupId != null)
            .Select(l => l.VatGroupId!.Value).ToListAsync(ct).ConfigureAwait(false);
        var theirVat = (await db.Set<VatGroup>().AsNoTracking().Where(v => v.OwnerId == company).ToListAsync(ct).ConfigureAwait(false))
            .Where(v => allowedVat.Count == 0 || allowedVat.Contains(v.Id)).ToList();

        var currencyIds = lines.Where(l => l.CurrencyId != null).Select(l => l.CurrencyId!.Value).Distinct().ToList();
        var currencyCodes = await db.Set<Currency>().AsNoTracking().Where(c => currencyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);
        var theirCurrencies = await db.Set<Currency>().AsNoTracking().Where(c => c.OwnerId == company || c.OwnerId == null).ToListAsync(ct).ConfigureAwait(false);

        var posting = new PostingService(db);
        var result = new List<TransactionDto>();
        foreach (var line in lines)
        {
            if (typeCodes.GetValueOrDefault(line.TypeId) is not { } typeCode || !theirTypes.TryGetValue(typeCode, out var type))
            {
                throw ValidationException.For("transactions", $"{ErrorCodes.NotConfiguredError}: the company does not have the line's transaction type.");
            }

            if (line.CurrencyId is not { } currency || currencyCodes.GetValueOrDefault(currency) is not { } currencyCode
                || theirCurrencies.FirstOrDefault(c => c.Code == currencyCode) is not { } theirCurrency)
            {
                throw ValidationException.For("transactions", $"{ErrorCodes.NotConfiguredError}: the company does not have the sale's currency.");
            }

            var product = line.ElementId is { } element && productCodes.TryGetValue(element, out var code) ? theirProducts.GetValueOrDefault(code) : null;
            // The line's kind in the company's books: of its transaction type, one holding the product, first the one that moves stock as the sale's did.
            var direction = line.TypeConfId is { } own ? directions.GetValueOrDefault(own) : 0;
            var candidates = confs[type].OrderBy(c => c.StockDirection == direction ? 0 : 1).ToList();
            Guid? conf = candidates.Count == 1 ? candidates[0].Id : null;
            if (product is not null)
            {
                foreach (var candidate in candidates)
                {
                    if (await posting.HoldsAsync(candidate.Scope, product.Id, ct).ConfigureAwait(false))
                    {
                        conf = candidate.Id;
                        break;
                    }
                }
            }

            var rate = line.VatGroupId is { } vat ? vatRates.GetValueOrDefault(vat) : null;
            var unit = line.UnitId is { } u && unitCodes.GetValueOrDefault(u) is { } unitCode && theirUnits.TryGetValue(unitCode, out var theirUnit) ? theirUnit : (Guid?)null;
            result.Add(new TransactionDto
            {
                TypeId = type, TypeConfId = conf, ElementId = product?.Id, ElementTypeId = product?.TypeId,
                // What the company has no product for keeps the group's name, so the company sees what to choose.
                ElementName = product?.Name.In(language) ?? line.ElementName,
                UnitId = unit, UnitName = unit is null ? line.UnitName : null, VatGroupId = rate is null ? null : theirVat.FirstOrDefault(v => v.Rate == rate)?.Id,
                CurrencyId = theirCurrency.Id, ExchangeRate = theirCurrency.IsBase ? 1 : null, Price = line.Price, Discount = line.Discount, Quantity = line.Quantity,
            });
        }

        return result;
    }
}

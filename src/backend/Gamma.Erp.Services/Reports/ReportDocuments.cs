using Gamma.Erp.Models.Documents;
using Gamma.Erp.Models.Employees;
using Gamma.Erp.Models.Partners;
using Gamma.Erp.Models.Transactions;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Reports;

/// <summary>A document type's posted documents in a period; a reversal type's count negative.</summary>
public sealed record DocumentTotalRowDto(Guid TypeId, string? Type, bool Reversal, int Documents, decimal Value, decimal Vat, decimal Total);

public sealed record DocumentTotalsDto(IReadOnlyList<DocumentTotalRowDto> Rows, int Documents, decimal Value, decimal Vat, decimal Total);

/// <summary>A contract: what it is worth, what was invoiced from it (posted documents, less their reversals) and what remains.</summary>
public sealed record ContractRowDto(Guid DocumentId, string? Document, string? Type, DateOnly? PostingDate, Guid? PartnerId, string? Partner, DateOnly? ValidUntil, decimal Value,
    decimal Invoiced, decimal Remaining);

/// <summary>Document totals, contracts and the staff ledger.</summary>
public sealed partial class ReportService
{
    /// <summary>The totals per document type of these categories in a period, posted documents only.</summary>
    public async Task<DocumentTotalsDto> DocumentTotalsAsync(IReadOnlyList<Guid> categoryIds, DateOnly from, DateOnly to, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(categoryIds);
        Period(from, to);
        var (included, reversals, names) = await TypesOfAsync(categoryIds, language, ct).ConfigureAwait(false);
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var (since, until) = (from, to);
        var rows = await (from d in db.Set<Document>().AsNoTracking()
                          join t in db.Set<Transaction>().AsNoTracking() on d.Id equals t.DocumentId
                          where d.Active && t.Active && d.Posted && included.Contains(d.TypeId) && companies.Contains(d.CompanyId) && d.PostingDate >= since && d.PostingDate <= until
                          select new { d.Id, d.TypeId, Value = t.ValueLcu ?? t.Value ?? 0, Vat = t.VatLcu ?? t.Vat ?? 0 }).ToListAsync(ct).ConfigureAwait(false);
        var totals = rows.GroupBy(r => r.TypeId).Select(g =>
        {
            var sign = reversals.Contains(g.Key) ? -1 : 1;
            var (value, vat) = (sign * g.Sum(r => r.Value), sign * g.Sum(r => r.Vat));
            return new DocumentTotalRowDto(g.Key, names.GetValueOrDefault(g.Key), sign < 0, g.Select(r => r.Id).Distinct().Count(), value, vat, value + vat);
        }).OrderBy(r => r.Reversal).ThenBy(r => r.Type).ToList();
        return new DocumentTotalsDto(totals, totals.Sum(r => r.Documents), totals.Sum(r => r.Value), totals.Sum(r => r.Vat), totals.Sum(r => r.Total));
    }

    /// <summary>
    /// The contracts of these categories in a period, one client or supplier or all. A contract is never posted, so drafts count, dated by their document date until a posting date is given. Its end date is the property its flows name.
    /// </summary>
    public async Task<IReadOnlyList<ContractRowDto>> ContractsAsync(IReadOnlyList<Guid> categoryIds, Guid? partnerId, DateOnly from, DateOnly to, string language, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(categoryIds);
        Period(from, to);
        var (included, reversals, names) = await TypesOfAsync(categoryIds, language, ct).ConfigureAwait(false);
        var types = included.Except(reversals).ToHashSet();
        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var contracts = await db.Set<Document>().AsNoTracking()
            .Where(d => d.Active && d.IdSup == null && types.Contains(d.TypeId) && companies.Contains(d.CompanyId) && (d.PostingDate ?? d.DocumentDate) >= from && (d.PostingDate ?? d.DocumentDate) <= to
                && (partnerId == null || d.FromId == partnerId || d.ToId == partnerId))
            .OrderBy(d => d.PostingDate ?? d.DocumentDate).ThenBy(d => d.Code).ToListAsync(ct).ConfigureAwait(false);
        var ids = contracts.Select(d => d.Id).ToList();
        var sides = contracts.SelectMany(d => new[] { d.FromId, d.ToId }).OfType<Guid>().Distinct().ToList();
        var partners = (await db.Set<Partner>().AsNoTracking().Where(p => sides.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();

        var links = await (from l in db.Set<InterDocumentDocument>().AsNoTracking()
                           join d in db.Set<Document>().AsNoTracking() on l.Document2Id equals d.Id
                           where l.Active && d.Active && d.Posted && l.Document1Id != null && ids.Contains(l.Document1Id.Value)
                           select new { Contract = l.Document1Id!.Value, Made = d.Id }).ToListAsync(ct).ConfigureAwait(false);
        var made = links.Select(l => l.Made).Distinct().ToList();
        var reversalsOf = await db.Set<Document>().AsNoTracking().Where(d => d.Active && d.Posted && d.ReversedDocumentId != null && made.Contains(d.ReversedDocumentId.Value))
            .Select(d => new { d.Id, Reversed = d.ReversedDocumentId!.Value }).ToListAsync(ct).ConfigureAwait(false);
        var worth = ids.Concat(made).Concat(reversalsOf.Select(r => r.Id)).Distinct().ToList();
        var values = (await db.Set<Transaction>().AsNoTracking().Where(t => t.Active && t.DocumentId != null && worth.Contains(t.DocumentId.Value))
                .Select(t => new { Document = t.DocumentId!.Value, Total = t.ValueVatLcu ?? t.ValueVat ?? 0 }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.Document).ToDictionary(g => g.Key, g => g.Sum(t => t.Total));
        decimal Net(Guid invoice) => values.GetValueOrDefault(invoice) - reversalsOf.Where(r => r.Reversed == invoice).Sum(r => values.GetValueOrDefault(r.Id));

        var ends = await EndDatesAsync(ids, types, ct).ConfigureAwait(false);
        return [.. contracts.Select(d =>
        {
            var partner = d.ToId is { } toPartner && partners.Contains(toPartner) ? toPartner : d.FromId is { } fromPartner && partners.Contains(fromPartner) ? fromPartner : (Guid?)null;
            var value = values.GetValueOrDefault(d.Id);
            var invoiced = links.Where(l => l.Contract == d.Id).Select(l => l.Made).Distinct().Sum(Net);
            return new ContractRowDto(d.Id, d.Code, names.GetValueOrDefault(d.TypeId), d.PostingDate ?? d.DocumentDate, partner, partner == d.ToId ? d.ToName : partner is null ? null : d.FromName,
                ends.TryGetValue(d.Id, out var end) ? end : null, value, invoiced, value - invoiced);
        })];
    }

    /// <summary>
    /// The documents behind a member of staff's balance in a period: the balance before the period, then each posted document with
    /// its debit, credit (company currency) and the running balance; null when the request does not see them.
    /// </summary>
    public async Task<IReadOnlyList<SideLedgerRowDto>?> EmployeeLedgerAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        Period(from, to);
        var nodes = await visibility.NodesAsync(ct).ConfigureAwait(false);
        var owner = await db.Set<Employee>().AsNoTracking().Where(e => e.Id == employeeId && e.Active).Select(e => new { e.OwnerId }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (owner is null || (owner.OwnerId is { } node && !nodes.Contains(node)))
        {
            return null;
        }

        var companies = await visibility.CompaniesAsync(ct).ConfigureAwait(false);
        var rows = await (from b in db.Set<EmployeeBalance>().AsNoTracking()
                          join d in db.Set<Document>().AsNoTracking() on b.DocumentId equals d.Id
                          where b.EmployeeId == employeeId && b.Active && d.Active && d.Posted && companies.Contains(d.CompanyId) && d.PostingDate <= to
                          select new { d.Id, d.Code, d.PostingDate, Debit = b.DebitLcu ?? 0, Credit = b.CreditLcu ?? 0 }).ToListAsync(ct).ConfigureAwait(false);
        var periods = await Gamma.Erp.Services.Currencies.CurrencyHistory.PeriodsAsync(db, companies, to, ct).ConfigureAwait(false);
        rows = [.. rows.Select(r => r with { Debit = Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(r.Debit, Gamma.Erp.Services.Currencies.CurrencyHistory.FactorOn(periods, r.PostingDate)), Credit = Gamma.Erp.Services.Currencies.CurrencyHistory.Scale(r.Credit, Gamma.Erp.Services.Currencies.CurrencyHistory.FactorOn(periods, r.PostingDate)) })];
        var balance = rows.Where(r => r.PostingDate < from).Sum(r => r.Debit - r.Credit);
        var ledger = new List<SideLedgerRowDto> { new(null, null, from, 0, 0, balance) };
        foreach (var d in rows.Where(r => r.PostingDate >= from).GroupBy(r => r.Id).Select(g => (g.First().Code, g.First().PostingDate, Id: g.Key, Debit: g.Sum(r => r.Debit), Credit: g.Sum(r => r.Credit)))
            .OrderBy(d => d.PostingDate).ThenBy(d => d.Id))
        {
            balance += d.Debit - d.Credit;
            ledger.Add(new SideLedgerRowDto(d.Id, d.Code, d.PostingDate, d.Debit, d.Credit, balance));
        }

        return ledger;
    }

    /// <summary>The document types of these categories with their reversal types, which of them reverse, and their names.</summary>
    private async Task<(HashSet<Guid> Included, HashSet<Guid> Reversals, Dictionary<Guid, string?> Names)> TypesOfAsync(IReadOnlyList<Guid> categoryIds, string language, CancellationToken ct)
    {
        if (categoryIds.Count == 0)
        {
            throw ValidationException.For("category", "Name at least one document category.");
        }

        var types = await db.Set<InterDocumentTypeCategory>().AsNoTracking().Where(l => categoryIds.Contains(l.CategoryId) && l.Active).Select(l => l.TypeId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var all = await db.Set<DocumentType>().AsNoTracking().Where(t => t.Active).ToListAsync(ct).ConfigureAwait(false);
        var reversals = all.Where(t => t.IsReversal).Select(t => t.Id).ToHashSet();
        var included = types.Concat(all.Where(t => t.IsReversal && t.ReversedDocumentTypeId is { } r && types.Contains(r)).Select(t => t.Id)).ToHashSet();
        return (included, reversals, all.ToDictionary(t => t.Id, t => t.Name.In(language)));
    }

    /// <summary>Each document's end date: the value of the property that the flows out of its type name.</summary>
    private async Task<Dictionary<Guid, DateOnly>> EndDatesAsync(List<Guid> documents, HashSet<Guid> types, CancellationToken ct)
    {
        var properties = await db.Set<DocumentTypeFlow>().AsNoTracking().Where(f => f.Active && f.ValidUntilInfoId != null && types.Contains(f.SourceTypeId))
            .Select(f => f.ValidUntilInfoId!.Value).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var values = await db.Set<DocumentInfo>().AsNoTracking().Where(v => v.Active && documents.Contains(v.ElementId) && properties.Contains(v.TypeInfoId))
            .Select(v => new { v.ElementId, v.Value }).ToListAsync(ct).ConfigureAwait(false);
        var ends = new Dictionary<Guid, DateOnly>();
        foreach (var v in values)
        {
            if (v.Value?.ToString() is { Length: >= 10 } text && DateOnly.TryParse(text[..10], System.Globalization.CultureInfo.InvariantCulture, out var day))
            {
                ends[v.ElementId] = day;
            }
        }

        return ends;
    }
}
